using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using iTunesLib;
using System.Diagnostics;
using System.Windows.Forms;
using System.Timers;

namespace iTunesController
{
    class iTunesControllerManager
    {
        TcpComServer _com;
        iTunesApp _iTunes;

        IITTrackCollection _songSearchResults;

        public delegate void ManagerConnectionStateChange(TcpComServer.ConnectionState connectionState, string description);
        public event ManagerConnectionStateChange ManagerConnectionStateChanged;

        private System.Timers.Timer _trackTimeTimer;
        

        public iTunesControllerManager()
        {
            StartITunesAPI();

            _com = new TcpComServer();
            _com.ServerConnectionStateChange += new TcpComServer.ConnectionStateChangeEventHander(_com_ServerConnectionStateChange);
            _com.PacketReady += new TcpComServer.PacketReadyEventHandler(ParsePacket);
            _com.ConnectionEvent += new TcpComServer.ConnectionEventHandler(_com_ConnectionEvent);

            _trackTimeTimer = new System.Timers.Timer(1000.0);
            _trackTimeTimer.Elapsed += new ElapsedEventHandler(_trackTimeTimer_Elapsed);
        }

        private void _trackTimeTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            NotifyPTime();
        }

        private void _com_ConnectionEvent(TcpComServer.ConnectionEventType connectionEvent, string data)
        {
            if (connectionEvent == TcpComServer.ConnectionEventType.PacketSent)
            {
                if (data == "welcome")
                {
                    //Debug.Print("got just before welcome routine");
                    PushAllDataToClientRoutine();
                }
            }
        }

        private void _com_ServerConnectionStateChange(TcpComServer.ConnectionState connectionState, string description)
        {
            ManagerConnectionStateChanged(connectionState, description);

            if (connectionState == TcpComServer.ConnectionState.Disconnected)
                _trackTimeTimer.Stop();
        }

        private void StartITunesAPI()
        {
            _iTunes = new iTunesApp();
            _iTunes.OnPlayerPlayEvent += new _IiTunesEvents_OnPlayerPlayEventEventHandler(_iTunes_OnPlayerPlayEvent);
            _iTunes.OnPlayerStopEvent += new _IiTunesEvents_OnPlayerStopEventEventHandler(_iTunes_OnPlayerStopEvent);
            _iTunes.OnSoundVolumeChangedEvent += new _IiTunesEvents_OnSoundVolumeChangedEventEventHandler(_iTunes_OnSoundVolumeChangedEvent);
            _iTunes.OnPlayerPlayingTrackChangedEvent += new _IiTunesEvents_OnPlayerPlayingTrackChangedEventEventHandler(_iTunes_OnPlayerPlayingTrackChangedEvent);
        }

        public bool Start()
        {
            try
            {
                _com.Start(9876);
                return true;
            }
            catch (Exception)
            {

                return false;
            }
        }

        void _iTunes_OnPlayerPlayingTrackChangedEvent(object iTrack)
        {
            NotifyRating();
            NotifyTrackName();
            StartTrackTimer();
        }

        private void StartTrackTimer()
        {
            _trackTimeTimer.Start();
            NotifyPTime();
        }

        private void _iTunes_OnSoundVolumeChangedEvent(int newVolume)
        {
            NotifyVolume(newVolume);
        }

        private void _iTunes_OnPlayerPlayEvent(object iTrack)
        {
            NotifyPlayPauseState();
            NotifyRating();
            NotifyTrackName();
            StartTrackTimer();

        }

        private void _iTunes_OnPlayerStopEvent(object iTrack)
        {
            NotifyPlayPauseState();
            _trackTimeTimer.Stop();
        }

        private void PushAllDataToClientRoutine()
        {
            NotifyPlayPauseState();
            NotifyVolume(_iTunes.SoundVolume);
            NotifyRating();
            NotifyTrackName();
            StartTrackTimer();
        }

        private void NotifyPlayPauseState()
        {
            if (_iTunes.PlayerState == ITPlayerState.ITPlayerStatePlaying)
                _com.SendPacket("information playpausestate playing");
            else
                _com.SendPacket("information playpausestate paused");
        }

        private void NotifyVolume(int newVolume)
        {
            _com.SendPacket("information volumestate " + newVolume.ToString());
        }

        private void NotifyRating()
        {
            try 
            {	        
	            int rating = _iTunes.CurrentTrack.Rating;
                _com.SendPacket("information rating " + rating.ToString());
            }
            catch (Exception e)
            {
        		
            }
        }

        private void NotifyTrackName()
        {
            try
            {
                string artist = _iTunes.CurrentTrack.Artist;
                string name = _iTunes.CurrentTrack.Name;
                _com.SendPacket("information trackname " + artist + " - " + name);
            }
            catch (Exception e)
            {

            }
        }

        private void NotifyPTime()
        {
            try
            {
                int duration = _iTunes.CurrentTrack.Duration;
                int pos = _iTunes.PlayerPosition;
                _com.SendPacket("information ptime " + duration.ToString() + " " + pos.ToString());
            }
            catch (Exception e)
            {

            }
        }

        private void ParsePacket(string packet)
        {
            string[] p = packet.Split(' ');

            if (p.Length > 0)
            {
                if (p[0] == "itunescommand")
                {
                    if (p[1] == "setvolume")
                    {
                        _iTunes.SoundVolume = Int32.Parse(p[2]);
                    }

                    if (p[1] == "playpause")
                    {
                        if (p[2] == "play")
                            _iTunes.Play();
                        else
                            _iTunes.Pause();
                    }

                    if (p[1] == "play")
                    {
                        if (p[2] == "next")
                            _iTunes.NextTrack();
                        else if (p[2] == "previous")
                            _iTunes.PreviousTrack();
                        else if (p[2] == "playbysearch")
                        {
                            //itunescommand play playbysearch antidote
                            InitiateSearchByText(packet, false);
                        }
                        else if (p[2] == "playbysearchlucky")
                        {
                            InitiateSearchByText(packet, true);
                        }
                        else
                        {
                            int which = Int32.Parse(p[2]);
                            _songSearchResults.get_ItemByPlayOrder(which).Play();
                        }
                    }
                    //itunescommand setrating 60
                    else if (p[1] == "setrating")
                    {
                        int rating = Int32.Parse(p[2]);
                        _iTunes.CurrentTrack.Rating = rating;
                    }
                    else if (p[1] == "setprogress")
                    {
                        int progress = Int32.Parse(p[2]);
                        _iTunes.PlayerPosition = progress;
                    }
                    else if (p[1] == "setplaylist")
                    {
                        _iTunes.LibrarySource.Playlists[Int32.Parse(p[2])].PlayFirstTrack();
                        //_iTunes.LibrarySource.playlistID = 1;
                    }
                }
                else if (p[0] == "request")
                {
                    if (p[1] == "playpausestate")
                    {
                        NotifyPlayPauseState();
                    }
                    if (p[1] == "volume")
                    {
                        NotifyVolume(_iTunes.SoundVolume);
                    }
                    if (p[1] == "playlists")
                    {
                        SendPlaylists();
                    }
                }
            }
        }

        public void Stop()
        {
            _com.Stop("Finished.");
        }

        private void InitiateSearchByText(string packet, bool feelingLucky)
        {
            //itunescommand play playbysearchlucky antidfasd
            string criteria;
            if (feelingLucky)
                criteria = packet.Substring(37);
            else
                criteria = packet.Substring(32);

            if (criteria != null)
            {
                if (_iTunes.CurrentPlaylist != null)
                {
                    if (criteria != "")
                    {
                        _songSearchResults = _iTunes.CurrentPlaylist.Search(criteria, ITPlaylistSearchField.ITPlaylistSearchFieldAll);
                        if (_songSearchResults != null)
                        {
                            if (_songSearchResults.Count == 0)
                            {
                                //no search results
                                _com.SendPacket("information searchresult none");
                            }
                            else if (_songSearchResults.Count > 1)
                            {
                                SendTracks();
                            }
                            else
                            {
                                //Only one result.
                                if (feelingLucky)
                                    _songSearchResults.get_ItemByPlayOrder(1).Play();
                                else
                                    SendTracks();
                            }
                        }
                        else
                        {
                            _com.SendPacket("information searchresult none");
                        }
                    }
                    else
                    {
                        //search text is blank, just send the whole playlist
                        _songSearchResults = _iTunes.CurrentPlaylist.Tracks;
                        SendTracks();
                    }
                }
            }
        }

        private void SendTracks()
        {
            //current, working version
            
            //send results to user
            List<string> packets = new List<String>();

            for (int i = 1; i <= _songSearchResults.Count; ++i)
            {
                string buff = "information searchresults ";
                buff = buff + (i - 1).ToString() + " " + _songSearchResults.get_ItemByPlayOrder(i).Artist + " - " + _songSearchResults.get_ItemByPlayOrder(i).Name;
                packets.Add(buff);
            }

            //done sending the search results, alert of the end of the search results
            packets.Add("information searchresultsend");

            _com.SendPackets(packets);
            
            


            //performance improvement attempt
            //cannot implement because java's BufferedReader can only support 8kB worth of characters
            /*
            //send results to user

            for (int i = 1; i <= _songSearchResults.Count; ++i)
            {
                string buff = "information searchresults ";
                buff = buff + (i - 1).ToString() + " " + 
                    _songSearchResults.get_ItemByPlayOrder(i).Artist + " - " + 
                    _songSearchResults.get_ItemByPlayOrder(i).Name + " - " + 
                    _com.GetEndOfPacketChar();
                //_com.SendPacket(buff);
            }

            //done sending the search results, alert of the end of the search results
            _com.SendPacket("information searchresultsend");
             * */
            
        }

        private void SendPlaylists()
        {
            //for (int i = 0; i < _iTunes.LibrarySource.Playlists.Count; ++i)
            //{
            //    string buff = "information playlists ";
            //    //buff = buff + i.ToString() + " " + _iTunes.


            //}
            //IITPlaylist playlist;

            for (int i = 1; i < _iTunes.LibrarySource.Playlists.Count; ++i)
            {
                string buff = "information playlistsearchresults ";
                buff += (i - 1).ToString() + " " + _iTunes.LibrarySource.Playlists[i].Name;
                _com.SendPacket(buff);
            }

            _com.SendPacket("information playlistsearchresultsend");


        }

        public void testing()
        {
            int i = 1;
            int j = 0;
            string results = "";
            while (true)
            {
                try
                {
                    results = results + '\n' + i.ToString() + ' ' + j.ToString() + ' ' + _iTunes.LibrarySource.Playlists.get_ItemByPersistentID(i, j).Name;
                    ++j;
                }
                catch
                {
                    break;
                }
                
            }

            MessageBox.Show(results);

            //IEnumberable<IITPlaylist> playlists = _iTunes.LibraryPlaylist.Source.Playlists.Cast<IITPlaylist>();

        }
    }
}

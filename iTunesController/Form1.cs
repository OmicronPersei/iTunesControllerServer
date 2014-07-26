using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace iTunesController
{
    public partial class Form1 : Form
    {
        //iTunesController _itc;
        iTunesControllerManager _icm;
        StartupManager _startup;
        
        public Form1()
        {
            InitializeComponent();
            
        }

        void _icm_ManagerConnectionStateChanged(TcpComServer.ConnectionState connectionState, string description)
        {
            try
            {
                lblState.Invoke((MethodInvoker)delegate { this.lblState.Text = description; });
            }
            catch (Exception)
            {
                
                
            }
            //this.lblState.Text = description; //This does not work, must use line above.
            
        }

       

        private void Form1_Load(object sender, EventArgs e)
        {
            _icm = new iTunesControllerManager();
            _icm.ManagerConnectionStateChanged += new iTunesControllerManager.ManagerConnectionStateChange(_icm_ManagerConnectionStateChanged);

            _startup = new StartupManager();
            if (_startup.isAlreadyStartup())
                chkStartup.Checked = true;

            if (!_icm.Start())
            {
                MessageBox.Show("Could not open the TCP server socket.");
            }



        }

        private void _tcs_PacketReady(string packet)
        {
            
        }



        private void chkStartup_CheckedChanged(object sender, EventArgs e)
        {
            if (chkStartup.Checked)
                _startup.addToStartup();
            else
                _startup.removeFromStartup();

        }

        private void button1_Click(object sender, EventArgs e)
        {
            _icm.testing();
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            _icm.Stop();
        }
    }
}

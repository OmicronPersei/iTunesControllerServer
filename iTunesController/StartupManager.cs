using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace iTunesController
{
    class StartupManager
    {
        const string _keyName = "iTunesControllerShortcut";

        private string _executablePath;

        private RegistryKey _rkApp;

        public StartupManager()
        {
            _executablePath = Application.ExecutablePath;

            _rkApp = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);

        }

        public void addToStartup()
        {
            if (!isAlreadyStartup())
            {
                //Not added to the startup, let's add it

                _rkApp.SetValue(_keyName, _executablePath);

            }

        }

        public void removeFromStartup()
        {
            if (isAlreadyStartup())
            {
                //Key exist, now lets delete it.

                _rkApp.DeleteValue(_keyName);
            }
        }

        public Boolean isAlreadyStartup()
        {
            return (_rkApp.GetValue(_keyName) == null) ? false : true;
        }
    }
}

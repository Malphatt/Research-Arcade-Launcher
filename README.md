# Research-Arcade-Launcher
 A WPF App for the University of Lincoln Research Arcade Machines.
## Setup
  Edit the Config.json file to your liking, the template file looks like this:
  ```
  {
    "UpdaterURL": "",
    "UpdaterVersionURL": "",
    "GameDatabaseURL": "",
    "NoInputTimeout_ms": 180000,
  
    "WS_Enabled": false,
    "WS_IP": "",
    "WS_Port": "",
    "AudioFilesURL": ""
  }
  ```
  If you leave it this way, it will probably crash, I didn't test it because I'm a professional.
  Everything in the lower half is not required so long as `WS_Enabled` is equal to `false`
## Information
  To run this system on an arcade machine you will need to launch this program using the [Research-Arcade-Updater](https://github.com/Malphatt/Research-Arcade-Updater "GitHub.com").
  For further information, please refer to the docs.
  

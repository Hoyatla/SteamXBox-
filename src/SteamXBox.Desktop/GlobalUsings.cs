// Les reglages, la traduction et la detection de peripherique vivent dans SteamXBox.Shell : le GUI
// et Desktop les partagent. Ce global using evite de les rappeler dans chaque fichier deplace.
global using SteamXBox.Shell.Configuration;
global using SteamXBox.Shell.Devices;
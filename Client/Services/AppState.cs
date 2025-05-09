// File: Client/Services/AppState.cs
namespace Safir.Client.Services
{
    public class AppState
    {
        public string? UUSER { get; private set; }
        public int USERCOD { get; set; }
        public int UGRP { get; set; }
        public string? USER_HES { get; private set; } // *** ADDED ***

        public void SetUUSER(string username) { UUSER = username; }
        public void SetUSERCOD(int userco) { USERCOD = userco; }
        public void SetUGRP(int userco) { UGRP = userco; }
        public void SetUSER_HES(string? userHes) { USER_HES = userHes; } // *** ADDED ***

        public Dictionary<string, string>? Settings { get; private set; }
        public void SetSettings(Dictionary<string, string> settings) { Settings = settings; }
    }
}
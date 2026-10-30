using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace pSB.GAMECORE
{
    public class GameSettingsManager : MonoBehaviour
    {
        //public bool customGame = false; //replaced by difficulty=0
        public SandGameTest.PSBGamemode activeGamemode = SandGameTest.PSBGamemode.Undefined;
        public int activeDifficulty = 0; // diffculty level used to fetch gamesettings preset; 0=custom

        private GameSettings customGameSettings = new GameSettings();
        void Start()
        {
        
        }

        public GameSettings GetGameSettings() {
            if(activeDifficulty == 0) {
                //custom user-defined settings apply
                return customGameSettings;
            }
            else {
                return GameSettingsDatabase.Instance.GetSettingsByGamemode(activeGamemode, activeDifficulty);
            }
        }

    }
}

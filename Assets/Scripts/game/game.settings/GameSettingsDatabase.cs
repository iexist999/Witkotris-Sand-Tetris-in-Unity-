using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace pSB.GAMECORE
{
    public class GameSettingsDatabase : MonoBehaviour
    {
        public static GameSettingsDatabase Instance { get; set; }

        [Header("=== DEFAULT PRESETS ===")]

        public List<GameSettingsProfile> gameProfiles = new List<GameSettingsProfile>();

        private void Awake() {
            if (Instance == null) { Instance = this; }
            else { Destroy(this); }
        }

        private void Start() {

        }

        public GameSettings GetSettingsByGamemode(SandGameTest.PSBGamemode gamemode, int difficulty) {
            for (int i = 0; i < gameProfiles.Count; i++) {
                if (gameProfiles[i].gamemode == gamemode && gameProfiles[i].difficultyLevel == difficulty) {
                    return gameProfiles[i].gameSettings;
                }
            }

            return null;
        }
    }
}

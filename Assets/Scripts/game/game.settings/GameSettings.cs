using SandBXDSIM;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace pSB.GAMECORE
{
    [System.Serializable]
    public class GameSettings
    {
        // classic settings
        public List<Color32> gameColors = new List<Color32>();
        public List<Sprite> gameShapes = new List<Sprite>();

        public float initialGameSpeed = 60f;
        public float initialGameSpeedMultiplier = 1f;
        public float initialBlockMoveSpeed = 3f;
        public float gameSpeedFactor = 1.0f; //1.0=standard speed

        public bool onlyCheckDownwardsMovement = false;

        // modificators
        public bool useModifiers = false;
        // avaiable modificators: lava, bomb, 
        public string[] activeModifiers = new string[] { "lava", "bomb" };

        public Sprite GetRandomShapeSprite() {
            return gameShapes[Random.Range(0, gameShapes.Count)];
        }

        public Color32 GetRandomColor() {
            return gameColors[Random.Range(0, gameColors.Count)]; 
        }

        public SpritePrinter.PrintSettings GetPrintSettingsForGame() {
            SpritePrinter.PrintSettings ps = new SpritePrinter.PrintSettings() {
                printPosition = Vector2.zero, //fixed
                printRotation = 0, //modifiable; to be changed randomly at spawn
                elementPrintMode = SpritePrinter.PrintSettings.ElementPrintMode.SingleElement,
                elementDistributionMode = SpritePrinter.PrintSettings.ElementDistributionMode.Random,
                elementArray = new (Color32?, string)[] { (null, "sand") },
                colorPrintMode = SpritePrinter.PrintSettings.ColorPrintMode.CustomDye,
                printDyeColor = null, //modifiable
            };
            return ps;
        }
    }

    [System.Serializable]
    public class GameSettingsProfile {
        public string profileName = string.Empty;
        public SandGameTest.PSBGamemode gamemode = SandGameTest.PSBGamemode.Undefined;

        public int difficultyLevel = -1; //undefined
        public GameSettings gameSettings = null;
    }
}

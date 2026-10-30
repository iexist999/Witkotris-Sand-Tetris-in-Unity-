using pSB.GAMECORE;
using SandBXDSIM;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SandGameTest : MonoBehaviour
{
    public enum PSBGamemode {
        Undefined,
        // classic game with sand blocks
        ClassicSand,

        // classic game with sand blocks featuring extra
        // modificators such as lava block or bombs
        ClassicSandPlus,

        // classic game with solid brick blocks.
        // these do not turn granular upon placement
        ClassicBricks,

        // classic game with solid brick blocks but they
        // are brittle and can partically crumble on collision
        BrittleBricks,

        // classic game that includes multiple elements.
        // each color gets assigned a custom random element at start
        ElementSoup,

        // classic mode with events that happen in random timespans
        // throughout the game. these include floods, solar eclipse, etc
        CHAOS,
    }

    [HideInInspector] public List<SandBlock> activeSandBlocks = new List<SandBlock>();
    [HideInInspector] public SandBXDSIM.SpritePrinter.PrintSettings printSettings = new SpritePrinter.PrintSettings();

    // game data
    public float blockMoveDelay = 3f;

    //references
    public GameSettingsManager gameSettingsManager;
    public SpritePrinter spritePrinter;
    public GameInput gameInput;
    //prv
    private float timer = 0f;
    private GameSettings currentGameSettings;

    // Start is called before the first frame update
    void Start()
    {
        StartGame();
    }

    public void StartGame() {
        if(gameSettingsManager.activeGamemode == PSBGamemode.ClassicSand) {
            currentGameSettings = gameSettingsManager.GetGameSettings();
            blockMoveDelay = currentGameSettings.initialGameSpeed;
            blockMoveDelay = (blockMoveDelay * (currentGameSettings.initialGameSpeed / 60f));
            //3 * (30 / 60)
        }

        SpawnNextShape();
    }

    // Update is called once per frame
    void Update()
    {
        timer += Time.deltaTime;

        if (timer >= 1f / blockMoveDelay) {
            timer = 0f;
            gameInput.HandleKeyboardInput();
        }
    }

    public void SpawnNextShape() {
        if(gameSettingsManager.activeGamemode == PSBGamemode.ClassicSand) {
            SpritePrinter.PrintSettings ps = currentGameSettings.GetPrintSettingsForGame();
            ps.printPosition = new Vector2(30, 180);
            ps.printRotation = 0;
            ps.printDyeColor = currentGameSettings.GetRandomColor();

            SandBlock newBlock = spritePrinter.PrintSpriteBlock(currentGameSettings.GetRandomShapeSprite(), ps, null);
            activeSandBlocks.Add(newBlock);
        }
    }

}

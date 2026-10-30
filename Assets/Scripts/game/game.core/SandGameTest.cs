using SandBXDSIM;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SandGameTest : MonoBehaviour
{
    public enum PSBGamemode {
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

    public PSBGamemode activeGamemode = PSBGamemode.ClassicSand;

    [HideInInspector] public List<SandBlock> activeSandBlocks = new List<SandBlock>();

    //references
    public SpritePrinter spritePrinter;

    // Start is called before the first frame update
    void Start()
    {
        
    }

    public void StartGame() {

    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void SpawnNextShape() {
        if(activeGamemode == PSBGamemode.ClassicSand) {

        }
    }

}

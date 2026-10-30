using SandBXDSIM;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace pSB.GAMECORE
{
    public class GameInput : MonoBehaviour
    {
        public SandGameTest sandGame;

        // Start is called before the first frame update
        void Start()
        {
        
        }

        // Update is called once per frame
        void Update()
        {
        
        }

        private Vector2 inputDir = Vector2.zero;

        public void HandleKeyboardInput() {
            if (sandGame.activeSandBlocks.Count <= 0) return;
            SandBlock block = sandGame.activeSandBlocks[0];

            GameSettings settings = sandGame.gameSettingsManager.GetGameSettings();
            bool onlyCheckDown = settings != null && settings.onlyCheckDownwardsMovement;

            int dx = 0;
            if (Input.GetKey(KeyCode.LeftArrow)) dx -= 1;
            if (Input.GetKey(KeyCode.RightArrow)) dx += 1;

            // sideways as its own call: map edge (OutOfBounds) is ignored,
            // Blocked means sand/stone/anchor is in the way
            int sideResult = dx != 0 ? block.TryMove(dx, 0) : SandBlock.MoveSuccess;
            bool hitSideObstacle = sideResult == SandBlock.MoveBlocked;

            bool landed;
            if (!onlyCheckDown && hitSideObstacle) {
                landed = true; // bumped into the pile sideways
            }
            else {
                // anything but success lands: Blocked = pile below, OutOfBounds = floor
                landed = block.TryMove(0, -1) != SandBlock.MoveSuccess;
            }

            if (landed) {
                block.DestroyBlock();
                sandGame.activeSandBlocks.RemoveAt(0);
                sandGame.SpawnNextShape();
            }
        }

    }
}

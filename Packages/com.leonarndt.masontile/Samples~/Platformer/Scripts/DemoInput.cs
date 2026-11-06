using UnityEngine;
#if MASONTILE_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MasonTile.Samples.Platformer
{
    /// <summary>Keyboard and gamepad input that works with either the Input System package or the legacy Input Manager.</summary>
    static class DemoInput
    {
#if MASONTILE_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
        public static float Horizontal
        {
            get
            {
                float x = 0;
                var k = Keyboard.current;
                if (k != null)
                {
                    if (k.aKey.isPressed || k.leftArrowKey.isPressed) x -= 1;
                    if (k.dKey.isPressed || k.rightArrowKey.isPressed) x += 1;
                }
                var pad = Gamepad.current;
                if (pad != null && Mathf.Abs(pad.leftStick.x.ReadValue()) > 0.3f) x = Mathf.Sign(pad.leftStick.x.ReadValue());
                return x;
            }
        }

        public static bool JumpPressed => Pressed(Keyboard.current?.spaceKey) || Pressed(Keyboard.current?.wKey) ||
                                          Pressed(Keyboard.current?.upArrowKey) || Pressed(Gamepad.current?.buttonSouth);

        public static bool JumpReleased => Released(Keyboard.current?.spaceKey) || Released(Keyboard.current?.wKey) ||
                                           Released(Keyboard.current?.upArrowKey) || Released(Gamepad.current?.buttonSouth);

        public static bool RestartPressed => Pressed(Keyboard.current?.rKey) || Pressed(Gamepad.current?.startButton);

        static bool Pressed(UnityEngine.InputSystem.Controls.ButtonControl b) => b != null && b.wasPressedThisFrame;
        static bool Released(UnityEngine.InputSystem.Controls.ButtonControl b) => b != null && b.wasReleasedThisFrame;
#else
        public static float Horizontal => Input.GetAxisRaw("Horizontal");
        public static bool JumpPressed => Input.GetButtonDown("Jump") || Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow);
        public static bool JumpReleased => Input.GetButtonUp("Jump") || Input.GetKeyUp(KeyCode.W) || Input.GetKeyUp(KeyCode.UpArrow);
        public static bool RestartPressed => Input.GetKeyDown(KeyCode.R);
#endif
    }
}

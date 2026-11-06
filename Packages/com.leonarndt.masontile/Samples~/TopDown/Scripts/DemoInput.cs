using UnityEngine;
#if MASONTILE_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MasonTile.Samples.TopDown
{
    /// <summary>Keyboard and gamepad input that works with either the Input System package or the legacy Input Manager.</summary>
    static class DemoInput
    {
        /// <summary>One of the four directions, or zero. Horizontal wins when both are held.</summary>
        public static Vector2Int Direction
        {
            get
            {
                var v = Raw();
                if (Mathf.Abs(v.x) > 0.3f) return new Vector2Int((int)Mathf.Sign(v.x), 0);
                if (Mathf.Abs(v.y) > 0.3f) return new Vector2Int(0, (int)Mathf.Sign(v.y));
                return Vector2Int.zero;
            }
        }

#if MASONTILE_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
        static Vector2 Raw()
        {
            var v = Vector2.zero;
            var k = Keyboard.current;
            if (k != null)
            {
                if (k.aKey.isPressed || k.leftArrowKey.isPressed) v.x -= 1;
                if (k.dKey.isPressed || k.rightArrowKey.isPressed) v.x += 1;
                if (k.sKey.isPressed || k.downArrowKey.isPressed) v.y -= 1;
                if (k.wKey.isPressed || k.upArrowKey.isPressed) v.y += 1;
            }
            var pad = Gamepad.current;
            if (pad != null && pad.leftStick.ReadValue().sqrMagnitude > 0.09f) v = pad.leftStick.ReadValue();
            return v;
        }

        public static bool InteractPressed => Pressed(Keyboard.current?.eKey) || Pressed(Keyboard.current?.spaceKey) ||
                                              Pressed(Keyboard.current?.enterKey) || Pressed(Gamepad.current?.buttonSouth);

        static bool Pressed(UnityEngine.InputSystem.Controls.ButtonControl b) => b != null && b.wasPressedThisFrame;
#else
        static Vector2 Raw() => new(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));

        public static bool InteractPressed => Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return);
#endif
    }
}

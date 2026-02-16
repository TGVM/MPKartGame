using UnityEngine;
using UnityEngine.InputSystem;

namespace Kart
{

    [CreateAssetMenu(fileName = "InputReader", menuName = "Kart/Input Reader")]
    public class InputReader : ScriptableObject, InputSystem_Actions.IPlayerActions
    {
        public Vector3 Move => inputActions.Player.Move.ReadValue<Vector2>();
        public bool Brake => inputActions.Player.Brake.ReadValue<float>() > 0;

        InputSystem_Actions inputActions;

        private void OnEnable()
        {
            if (inputActions == null) { 
                inputActions = new InputSystem_Actions();
                inputActions.Player.SetCallbacks(this);
            }
        }

        public void Enable()
        {
            inputActions.Enable();
        }

        public void OnBrake(InputAction.CallbackContext context)
        {
            // noop
        }

        public void OnFire(InputAction.CallbackContext context)
        {
            // noop
        }

        public void OnLook(InputAction.CallbackContext context)
        {
            // noop
        }

        public void OnMove(InputAction.CallbackContext context)
        {
            // noop
        }
    }
}

using System;
using TMPro;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace StarterAssets
{
    [RequireComponent(typeof(CharacterController))]
#if ENABLE_INPUT_SYSTEM
    [RequireComponent(typeof(PlayerInput))]
#endif
    public class FirstPersonControllerModified : MonoBehaviour
    {
        [Header("Player")]
        [Tooltip("Move speed of the character in m/s")]
        public float MoveSpeed = 8.0f;
        [Tooltip("Multiplier to reduce speed in the air")]
        public float AirSpeedMultiplier = 1f;
        [Tooltip("Dash Speed multiplier which gets added to MoveSpeed")]
        public float DashSpeedMultiplier = 5f;
        [Tooltip("How long does the Dash Last")]
        public float DashDuration = 0.07f;
        [Tooltip("Time Required before player can dash again")]
        public float DashCooldown = 1f;

        [Tooltip("Rotation speed of the character")]
        public float RotationSpeed = 1.0f;
        [Tooltip("Acceleration and deceleration")]
        public float SpeedChangeRate = 10.0f;

        [Space(10)]
        [Tooltip("The height the player can jump")]
        public float JumpHeight = 1.2f;
        [Tooltip("The character uses its own gravity value. The engine default is -9.81f")]
        public float Gravity = -15.0f;
        [Tooltip("The multiplier that affects Gravity when player is sliding down a wall")]
        public float WallSlideMultiplier = 0.5f;
        [Tooltip("The Number of times a player can Wall Jump")]
        public int WallJumpNumber = 2;
        [Tooltip("How Long Player can Wallrun")]
        public float WallRunDuration = 1f;

        [Space(10)]
        [Tooltip("Time required to pass before being able to jump again. Set to 0f to instantly jump again")]
        public float JumpTimeout = 0.1f;
        [Tooltip("Time required to pass before entering the fall state. Useful for walking down stairs")]
        public float FallTimeout = 0.15f;

        [Header("Player Grounded")]
        [Tooltip("If the character is grounded or not. Not part of the CharacterController built in grounded check")]
        public bool Grounded = true;
        [Tooltip("If the character is touching a wall or not")]
        public bool Walled = false;
        [Tooltip("Useful for rough ground")]
        public float GroundedOffset = -0.14f;
        [Tooltip("The radius of the grounded check. Should match the radius of the CharacterController")]
        public float GroundedRadius = 0.5f;
        [Tooltip("What layers the character uses as ground")]
        public LayerMask GroundLayers;

        [Header("Cinemachine")]
        [Tooltip("The follow target set in the Cinemachine Virtual Camera that the camera will follow")]
        public GameObject CinemachineCameraTarget;
        [Tooltip("How far in degrees can you move the camera up")]
        public float TopClamp = 90.0f;
        [Tooltip("How far in degrees can you move the camera down")]
        public float BottomClamp = -90.0f;

        // cinemachine
        private float _cinemachineTargetPitch;

        //State Machines
        enum MovementState
        {
            Grounded,
            Falling,
            Dashing,
            Walling,
        }

        // player
        private float _speed;
        private float _rotationVelocity;
        private float _verticalVelocity;
        private float _horizontalVelocity;
        private float _terminalVelocity = 53.0f;
        private float _prevSpeed;
        private Vector3 _prevDirection;
        private Vector3 _currentDirection;

        private MovementState _currentMovementState = MovementState.Grounded;

        //Booleans
        private bool _wallJumping = false;

        private int currentNumberOfWallJumps = 0;

        // timeout deltatime
        private float _dashDurationDelta;
        private float _dashTimeoutDelta;
        private float _jumpTimeoutDelta;
        private float _fallTimeoutDelta;
        private float _wallRunDelta;





#if ENABLE_INPUT_SYSTEM
        private PlayerInput _playerInput;
#endif
        private CharacterController _controller;
        private StarterAssetsInputs _input;
        private GameObject _mainCamera;

        private const float _threshold = 0.01f;

        private bool IsCurrentDeviceMouse
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                return _playerInput.currentControlScheme == "KeyboardMouse";
#else
				return false;
#endif
            }
        }

        private void Awake()
        {
            // get a reference to our main camera
            if (_mainCamera == null)
            {
                _mainCamera = GameObject.FindGameObjectWithTag("MainCamera");
            }
        }

        private void Start()
        {
            _controller = GetComponent<CharacterController>();
            _input = GetComponent<StarterAssetsInputs>();
#if ENABLE_INPUT_SYSTEM
            _playerInput = GetComponent<PlayerInput>();
#else
			Debug.LogError( "Starter Assets package is missing dependencies. Please use Tools/Starter Assets/Reinstall Dependencies to fix it");
#endif

            // reset our timeouts on start
            _jumpTimeoutDelta = JumpTimeout;
            _fallTimeoutDelta = FallTimeout;
        }

        private void Update()
        {
            UpdateCooldowns();
            JumpAndGravity();
            HorizontalMove();

            MoveThePlayer(_currentDirection);
        }

        private void LateUpdate()
        {
            CameraRotation();
        }

        private void UpdateStates()
        {
            //RETENTION CHECKS
            //Still dashing
            if (_dashDurationDelta <= DashDuration)
                return;


            //TRANSITION CHECKS
            // set sphere position, with offset
            Vector3 spherePosition = new Vector3(transform.position.x, transform.position.y - GroundedOffset, transform.position.z);
            //GroundCheck
            if (Physics.CheckSphere(spherePosition, GroundedRadius, GroundLayers, QueryTriggerInteraction.Ignore))
                _currentMovementState = MovementState.Grounded;

            //Wall Check
            else if (_currentMovementState != MovementState.Walling || _currentMovementState != MovementState.Grounded)
            {
                if (Physics.CheckSphere(spherePosition + new Vector3(0, 1f, 0), GroundedRadius + 0.1f, GroundLayers, QueryTriggerInteraction.Ignore))
                    _currentMovementState = MovementState.Walling;
            }

            //Dash Check
            else if (_input.dash && _dashTimeoutDelta <= 0.0f)
                _currentMovementState = MovementState.Dashing;

            //Falling State
            else
                _currentMovementState = MovementState.Falling;
        }

        private void CameraRotation()
        {
            // if there is an input
            if (_input.look.sqrMagnitude >= _threshold)
            {
                //Don't multiply mouse input by Time.deltaTime
                float deltaTimeMultiplier = IsCurrentDeviceMouse ? 1.0f : Time.deltaTime;

                _cinemachineTargetPitch += _input.look.y * RotationSpeed * deltaTimeMultiplier;
                _rotationVelocity = _input.look.x * RotationSpeed * deltaTimeMultiplier;

                // clamp our pitch rotation
                _cinemachineTargetPitch = ClampAngle(_cinemachineTargetPitch, BottomClamp, TopClamp);

                // Update Cinemachine camera target pitch
                CinemachineCameraTarget.transform.localRotation = Quaternion.Euler(_cinemachineTargetPitch, 0.0f, 0.0f);

                // rotate the player left and right
                transform.Rotate(Vector3.up * _rotationVelocity);
            }
        }

        private void UpdateCooldowns()
        {
            if (_dashTimeoutDelta >= 0.0f)
                _dashTimeoutDelta -= Time.deltaTime;

            if (_jumpTimeoutDelta >= 0.0f)
                _jumpTimeoutDelta -= Time.deltaTime;
        }

        private void HorizontalMove()
        {
            //Update the states
            UpdateStates();

            //Handle the movement based on the current state
            Vector3 inputDirection = Vector3.zero;
            switch (_currentMovementState)
            {
                case MovementState.Grounded:
                    _currentDirection = HandleGroundHoriMovement();
                    break;
                case MovementState.Walling:
                    _currentDirection = HandleWalledHoriMovement();
                    break;
                case MovementState.Falling:
                    _currentDirection = HandleFallingHoriMovement();
                    break;
                case MovementState.Dashing:
                    _dashTimeoutDelta = DashCooldown;
                    _dashDurationDelta = 0f;
                    _currentDirection = HandleDashHoriMovement();
                    break;
                default:
                    break;
            }
        }

        private Vector3 GeneralHorizontalMovement(float targetSpeed)
        {
            if (_input.move == Vector2.zero) targetSpeed = 0.0f;

            //Get reference to current player speed
            float currentHorizontalSpeed = new Vector3(_controller.velocity.x, 0.0f, _controller.velocity.z).magnitude;
            float speedOffset = 0.1f;
            float inputMagnitude = _input.analogMovement ? _input.move.magnitude : 1f;

            //Small bit of hard coding for dashing
            float speedChangeRate = _currentMovementState == MovementState.Dashing ? SpeedChangeRate * 100 : SpeedChangeRate;

            // accelerate or decelerate to target speed
            if (currentHorizontalSpeed < targetSpeed - speedOffset || currentHorizontalSpeed > targetSpeed + speedOffset)
            {
                //Lerp
                _speed = Mathf.Lerp(currentHorizontalSpeed, targetSpeed * inputMagnitude, Time.deltaTime * speedChangeRate);
                // round speed to 3 decimal places
                _speed = Mathf.Round(_speed * 1000f) / 1000f;
            }
            else
            {
                _speed = targetSpeed;
            }

            // Get normalized input direction
            Vector3 inputDirection = new Vector3(_input.move.x, 0.0f, _input.move.y).normalized;
            if (_input.move != Vector2.zero)
            {
                inputDirection = transform.right * _input.move.x + transform.forward * _input.move.y;
            }

            return inputDirection;
        }

        private Vector3 HandleGroundHoriMovement()
        {
            //Set target speed
            float targetSpeed = MoveSpeed;
            return GeneralHorizontalMovement(MoveSpeed);

        }
        private Vector3 HandleWalledHoriMovement()
        {
            //Retention of Wall Jumping state
            if (_wallJumping)
            {
                return _prevDirection;
            }
            //Initializing WallJump state
            Vector3 inputDirection = Vector3.zero;
            if (_input.jump && _jumpTimeoutDelta <= 0.0f && currentNumberOfWallJumps < WallJumpNumber)
            {
                //Finding location of the wall
                RaycastHit hit;
                for (int i = -1; i < 2; i += 2)
                {
                    if (Physics.SphereCast(transform.position, GroundedRadius, (transform.forward * i), out hit, 0.02f, GroundLayers))
                    {
                        inputDirection = hit.normal;
                        break;
                    }
                    else if (Physics.SphereCast(transform.position, GroundedRadius, (transform.right * i), out hit, 0.02f, GroundLayers))
                    {
                        inputDirection = hit.normal;
                        break;
                    }
                }
                float targetSpeed = MoveSpeed;
                currentNumberOfWallJumps++;
                _input.jump = false;

                float currentHorizontalSpeed = new Vector3(_controller.velocity.x, 0.0f, _controller.velocity.z).magnitude;
                float speedOffset = 0.1f;

                if (currentHorizontalSpeed < targetSpeed - speedOffset || currentHorizontalSpeed > targetSpeed + speedOffset)
                {
                    _speed = Mathf.Lerp(currentHorizontalSpeed, targetSpeed, Time.deltaTime * SpeedChangeRate);
                    _speed = Mathf.Round(_speed * 1000f) / 1000f;
                }
                else
                {
                    _speed = targetSpeed;
                }
                _wallJumping = true;
                _prevDirection = inputDirection;
                return inputDirection;
            }
            //Wall run state
            if (_input.move != Vector2.zero && _wallRunDelta >= 0.0f)
            {
                inputDirection += transform.right * _input.move.x + transform.forward * _input.move.y;
                // Cos 30 so that we have a arc of 60 degrees where we are allowed to climb up (Hyp = Adj / cosQ)
                float lengthOfRaycast = GroundedRadius / 0.866f;

                if (Physics.Raycast(transform.position, inputDirection, lengthOfRaycast, GroundLayers, QueryTriggerInteraction.Ignore))
                {
                    _verticalVelocity = Mathf.Sqrt(JumpHeight * -1f * Gravity); //Half Jump height
                }
                _wallRunDelta -= Time.deltaTime;
                return inputDirection;
            }
            else
                return Vector3.zero;
        }

        private Vector3 HandleFallingHoriMovement()
        {
            //Set target speed
            float targetSpeed = _controller.velocity.y <= 0.0f ? MoveSpeed * AirSpeedMultiplier : MoveSpeed;
            return GeneralHorizontalMovement(targetSpeed);
        }

        private Vector3 HandleDashHoriMovement()
        {
            _input.dash = false;
            _dashDurationDelta += Time.deltaTime;
            float targetSpeed = MoveSpeed * DashSpeedMultiplier;
            return GeneralHorizontalMovement(targetSpeed);

        }

        private void JumpAndGravity()
        {
            if (Grounded)
            {
                currentNumberOfWallJumps = 0;
                _wallRunDelta = WallRunDuration;
                // reset the fall timeout timer
                _fallTimeoutDelta = FallTimeout;

                // stop our velocity dropping infinitely when grounded
                if (_verticalVelocity < 0.0f)
                {
                    _verticalVelocity = -2f;
                }
                // Jump
                if (_input.jump && _jumpTimeoutDelta <= 0.0f)
                {
                    // the square root of H * -2 * G = how much velocity needed to reach desired height
                    _verticalVelocity = Mathf.Sqrt(JumpHeight * -2f * Gravity);
                    _input.jump = false;
                }

                // jump timeout
                if (_jumpTimeoutDelta >= 0.0f)
                {
                    _jumpTimeoutDelta -= Time.deltaTime;
                }
            }
            else if (!Walled)
            {
                // reset the jump timeout timer
                _jumpTimeoutDelta = JumpTimeout;

                // fall timeout
                if (_fallTimeoutDelta >= 0.0f)
                {
                    _fallTimeoutDelta -= Time.deltaTime;
                }

                // if we are not grounded, do not jump
                _input.jump = false;
            }

            // apply gravity over time if under terminal (multiply by delta time twice to linearly speed up over time)
            if (_verticalVelocity < _terminalVelocity)
            {
                if (!Walled)
                    _verticalVelocity += Gravity * Time.deltaTime;
                else
                    _verticalVelocity += Gravity * Time.deltaTime * WallSlideMultiplier;
            }
        }

        void MoveThePlayer(Vector3 Direction)
        {
            _controller.Move(Direction.normalized * (_speed * Time.deltaTime) + new Vector3(0.0f, _verticalVelocity, 0.0f) * Time.deltaTime);
        }

        private static float ClampAngle(float lfAngle, float lfMin, float lfMax)
        {
            if (lfAngle < -360f) lfAngle += 360f;
            if (lfAngle > 360f) lfAngle -= 360f;
            return Mathf.Clamp(lfAngle, lfMin, lfMax);
        }

        private void OnDrawGizmosSelected()
        {
            Color transparentGreen = new Color(0.0f, 1.0f, 0.0f, 0.35f);
            Color transparentRed = new Color(1.0f, 0.0f, 0.0f, 0.35f);

            if (Grounded) Gizmos.color = transparentGreen;
            else Gizmos.color = transparentRed;

            // when selected, draw a gizmo in the position of, and matching radius of, the grounded collider
            Gizmos.DrawSphere(new Vector3(transform.position.x, transform.position.y - GroundedOffset, transform.position.z), GroundedRadius);
        }
    }
}
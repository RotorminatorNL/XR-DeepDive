using System;
using UnityEngine;

public class CharacterMovement : MonoBehaviour
{
    /// <summary>
    /// The speed multiplier of movement.
    /// </summary>
    public float MovementSpeed = 8f;

    /// <summary>
    /// The rate acceleration during movement.
    /// </summary>
    public float Acceleration = 0.1f;

    /// <summary>
    /// The rate of damping on movement.
    /// </summary>
    public float Damping = 0.3f;

    /// <summary>
    /// The rate of additional damping when moving sideways or backwards.
    /// </summary>
    public float BackAndSideDampen = 0.5f;

    /// <summary>
    /// The force applied to the character when jumping.
    /// </summary>
    public float JumpForce = 0.3f;

    /// <summary>
    /// The rate of rotation when using a gamepad.
    /// </summary>
    public float RotationAmount = 1.5f;

    /// <summary>
    /// The rate of rotation when using the keyboard.
    /// </summary>
    public float RotationRatchet = 45.0f;

    /// <summary>
    /// The player will rotate in fixed steps if Snap Rotation is enabled.
    /// </summary>
    [Tooltip("The player will rotate in fixed steps if Snap Rotation is enabled.")]
    public bool SnapRotation = true;

    /// <summary>
    /// If true, each OVRPlayerController will use the player's physical height.
    /// </summary>
    public bool useProfileData = true;

    /// <summary>
    /// [Deprecated] When enabled, snap rotation will happen about the guardian rather
    /// than the player/camera viewpoint.
    /// </summary>
    [Tooltip("[Deprecated] When enabled, snap rotation will happen about the center of the " +
             "guardian rather than the center of the player/camera viewpoint. This (legacy) " +
             "option should be left off except for edge cases that require extreme behavioral " +
             "backwards compatibility.")]
    public bool RotateAroundGuardianCenter = true;

    /// <summary>
    /// How many fixed speeds to use with linear movement? 0=linear control
    /// </summary>
    [Tooltip("How many fixed speeds to use with linear movement? 0=linear control")]
    public int FixedSpeedSteps;

    /// <summary>
    /// If true, reset the initial yaw of the player controller when the Hmd pose is recentered.
    /// </summary>
    public bool HmdResetsY = true;

    /// <summary>
    /// If true, tracking data from a child OVRCameraRig will update the direction of movement.
    /// </summary>
    public bool HmdRotatesY = true;

    /// <summary>
    /// Modifies the strength of gravity.
    /// </summary>
    public float GravityModifier = 0.379f;

    /// <summary>
    /// When true, user input will be applied to linear movement. Set this to false whenever the player controller needs to ignore input for
    /// linear movement.
    /// </summary>
    public bool EnableLinearMovement = true;

    /// <summary>
    /// When true, user input will be applied to rotation. Set this to false whenever the player controller needs to ignore input for rotation.
    /// </summary>
    public bool EnableRotation = true;

    /// <summary>
    /// Rotation defaults to secondary thumbstick. You can allow either here. Note that this won't behave well if EnableLinearMovement is true.
    /// </summary>
    public bool RotationEitherThumbstick = false;

    /// <summary>
    /// The CameraHeight is the actual height of the HMD and can be used to adjust the height of the character controller, which will affect the
    /// ability of the character to move into areas with a low ceiling.
    /// </summary>
    [NonSerialized]
    public float CameraHeight;

    /// <summary>
    /// This bool is set to true whenever the player controller has been teleported. It is reset after every frame. Some systems, such as
    /// CharacterCameraConstraint, test this boolean in order to disable logic that moves the character controller immediately
    /// following the teleport.
    /// </summary>
    [NonSerialized] // This doesn't need to be visible in the inspector.
    public bool Teleported;

    /// <summary>
    /// This event is raised after the character controller is moved. This is used by the OVRAvatarLocomotion script to keep the avatar transform synchronized
    /// with the OVRPlayerController.
    /// </summary>
    public event Action<Transform> TransformUpdated;

    /// <summary>
    /// This event is raised immediately after the camera transform has been updated, but before movement is updated.
    /// </summary>
    public event Action CameraUpdated;

    /// <summary>
    /// This event is raised right before the character controller is actually moved in order to provide other systems the opportunity to
    /// move the character controller in response to things other than user input, such as movement of the HMD. See CharacterCameraConstraint.cs
    /// for an example of this.
    /// </summary>
    public event Action PreCharacterMove;

    protected Rigidbody Rb;
    protected CapsuleCollider RbCol;
    protected OVRCameraRig CameraRig;

    public float InitialYRotation { get; private set; }

    private bool playerControllerEnabled = false;
    private OVRPose? InitialPose;
    private float SimulationRate = 60f;
    private Vector3 MoveThrottle = Vector3.zero;
    private bool HaltUpdateMovement = false;
    private float MoveScale = 1.0f;
    private float MoveScaleMultiplier = 1.0f;
    private float RotationScaleMultiplier = 1.0f;
    private bool prevHatLeft = false;
    private bool prevHatRight = false;
    private bool isGrounded = true;
    private float buttonRotation = 0f;

    // Set to true when a snap turn has occurred, code requires one frame of centered thumbstick to enable another snap turn.
    private bool ReadyToSnapTurn;

    void Start()
    {
        // Add eye-depth as a camera offset from the player controller
        var p = CameraRig.transform.localPosition;
        p.z = OVRManager.profile.eyeDepth;
        CameraRig.transform.localPosition = p;
    }

    void Awake()
    {
        Rb = GetComponent<Rigidbody>();
        RbCol = GetComponent<CapsuleCollider>();

        if (Rb == null)
            Debug.LogWarning("CharacterMovement: No Rigidbody attached.");
        if(RbCol == null)
            Debug.LogWarning("CharacterMovement: No CapsuleCollider attached.");

        // We use OVRCameraRig to set rotations to cameras,
        // and to be influenced by rotation
        OVRCameraRig[] CameraRigs = gameObject.GetComponentsInParent<OVRCameraRig>();

        if (CameraRigs.Length == 0)
            Debug.LogWarning("CharacterMovement: No OVRCameraRig attached.");
        else if (CameraRigs.Length > 1)
            Debug.LogWarning("CharacterMovement: More then 1 OVRCameraRig attached.");
        else
            CameraRig = CameraRigs[0];

        InitialYRotation = transform.rotation.eulerAngles.y;
    }

    void OnDisable()
    {
        if (playerControllerEnabled)
        {
            OVRManager.display.RecenteredPose -= ResetOrientation;

            if (CameraRig != null)
            {
                CameraRig.UpdatedAnchors -= UpdateTransform;
            }

            playerControllerEnabled = false;
        }
    }

    private void Update()
    {
        if (!playerControllerEnabled)
        {
            if (OVRManager.OVRManagerinitialized)
            {
                OVRManager.display.RecenteredPose += ResetOrientation;

                if (CameraRig != null)
                {
                    CameraRig.UpdatedAnchors += UpdateTransform;
                }

                playerControllerEnabled = true;
            }
            else
                return;
        }
    }

    /// <summary>
    /// Invoked by OVRCameraRig's UpdatedAnchors callback. Allows the Hmd rotation to update the facing direction of the player.
    /// </summary>
    public void UpdateTransform(OVRCameraRig rig)
    {
        Transform root = CameraRig.trackingSpace;
        Transform centerEye = CameraRig.centerEyeAnchor;

        if (HmdRotatesY && !Teleported)
        {
            Vector3 prevPos = root.position;
            Quaternion prevRot = root.rotation;

            transform.rotation = Quaternion.Euler(0.0f, centerEye.rotation.eulerAngles.y, 0.0f);

            root.SetPositionAndRotation(prevPos, prevRot);
        }

        UpdateController();
        if (TransformUpdated != null)
        {
            TransformUpdated(root);
        }
    }

    protected virtual void UpdateController()
    {
        if (useProfileData)
        {
            if (InitialPose == null)
            {
                // Save the initial pose so it can be recovered if useProfileData
                // is turned off later.
                InitialPose = new OVRPose()
                {
                    position = CameraRig.transform.localPosition,
                    orientation = CameraRig.transform.localRotation
                };
            }

            var p = CameraRig.transform.localPosition;
            if (OVRManager.instance.trackingOriginType == OVRManager.TrackingOrigin.EyeLevel)
            {
                p.y = OVRManager.profile.eyeHeight - (0.5f * RbCol.height) + RbCol.center.y;
            }
            else if (OVRManager.instance.trackingOriginType == OVRManager.TrackingOrigin.FloorLevel)
            {
                p.y = -(0.5f * RbCol.height) + RbCol.center.y;
            }

            CameraRig.transform.localPosition = p;
        }
        else if (InitialPose != null)
        {
            // Return to the initial pose if useProfileData was turned off at runtime
            CameraRig.transform.localPosition = InitialPose.Value.position;
            CameraRig.transform.localRotation = InitialPose.Value.orientation;
            InitialPose = null;
        }

        CameraHeight = CameraRig.centerEyeAnchor.localPosition.y;

        if (CameraUpdated != null)
        {
            CameraUpdated();
        }

        UpdateMovement();

        Vector3 moveDirection = Vector3.zero;

        float motorDamp = (1.0f + (Damping * SimulationRate * Time.deltaTime));

        MoveThrottle.x /= motorDamp;
        MoveThrottle.y = (MoveThrottle.y > 0.0f) ? (MoveThrottle.y / motorDamp) : MoveThrottle.y;
        MoveThrottle.z /= motorDamp;

        moveDirection += MoveThrottle * SimulationRate * Time.deltaTime;

        if (PreCharacterMove != null)
        {
            PreCharacterMove();
            Teleported = false;
        }

        Vector3 predictedXZ = Vector3.Scale((Rb.transform.localPosition + moveDirection), new Vector3(1, 0, 1));

        // Move contoller
        Rb.MovePosition(transform.position + (moveDirection * MovementSpeed * 100f * Time.deltaTime));
        
        Vector3 actualXZ = Vector3.Scale(Rb.transform.localPosition, new Vector3(1, 0, 1));

        if (predictedXZ != actualXZ)
            MoveThrottle += (actualXZ - predictedXZ) / (SimulationRate * Time.deltaTime);
    }

    public virtual void UpdateMovement()
    {
        //todo: enable for Unity Input System
#if ENABLE_LEGACY_INPUT_MANAGER
        if (HaltUpdateMovement)
            return;

        if (EnableLinearMovement)
        {
            bool moveForward = Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow);
            bool moveLeft = Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow);
            bool moveRight = Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow);
            bool moveBack = Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow);

            bool dpad_move = false;

            if (OVRInput.Get(OVRInput.Button.DpadUp))
            {
                moveForward = true;
                dpad_move = true;
            }

            if (OVRInput.Get(OVRInput.Button.DpadDown))
            {
                moveBack = true;
                dpad_move = true;
            }

            MoveScale = 1.0f;

            if ((moveForward && moveLeft) || (moveForward && moveRight) ||
                (moveBack && moveLeft) || (moveBack && moveRight))
                MoveScale = 0.70710678f;

            // No positional movement if we are in the air
            if (!isGrounded)
                MoveScale = 0.0f;

            MoveScale *= SimulationRate * Time.deltaTime;

            // Compute this for key movement
            float moveInfluence = Acceleration * 0.1f * MoveScale * MoveScaleMultiplier;

            // Run!
            if (dpad_move || Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                moveInfluence *= 2.0f;

            Quaternion ort = transform.rotation;
            Vector3 ortEuler = ort.eulerAngles;
            ortEuler.z = ortEuler.x = 0f;
            ort = Quaternion.Euler(ortEuler);

            if (moveForward)
                MoveThrottle += ort * (transform.lossyScale.z * moveInfluence * Vector3.forward);
            if (moveBack)
                MoveThrottle += ort * (transform.lossyScale.z * moveInfluence * BackAndSideDampen * Vector3.back);
            if (moveLeft)
                MoveThrottle += ort * (transform.lossyScale.x * moveInfluence * BackAndSideDampen * Vector3.left);
            if (moveRight)
                MoveThrottle += ort * (transform.lossyScale.x * moveInfluence * BackAndSideDampen * Vector3.right);

            moveInfluence = Acceleration * 0.1f * MoveScale * MoveScaleMultiplier;

            Vector2 primaryAxis = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick);

            // If speed quantization is enabled, adjust the input to the number of fixed speed steps.
            if (FixedSpeedSteps > 0)
            {
                primaryAxis.y = Mathf.Round(primaryAxis.y * FixedSpeedSteps) / FixedSpeedSteps;
                primaryAxis.x = Mathf.Round(primaryAxis.x * FixedSpeedSteps) / FixedSpeedSteps;
            }

            if (primaryAxis.y > 0.0f)
                MoveThrottle += ort * (primaryAxis.y * transform.lossyScale.z * moveInfluence * Vector3.forward);

            if (primaryAxis.y < 0.0f)
                MoveThrottle += ort * (Mathf.Abs(primaryAxis.y) * transform.lossyScale.z * moveInfluence *
                                       BackAndSideDampen * Vector3.back);

            if (primaryAxis.x < 0.0f)
                MoveThrottle += ort * (Mathf.Abs(primaryAxis.x) * transform.lossyScale.x * moveInfluence *
                                       BackAndSideDampen * Vector3.left);

            if (primaryAxis.x > 0.0f)
                MoveThrottle += ort * (primaryAxis.x * transform.lossyScale.x * moveInfluence * BackAndSideDampen *
                                       Vector3.right);
        }

        if (EnableRotation)
        {
            Vector3 euler = RotateAroundGuardianCenter ? transform.rotation.eulerAngles : Vector3.zero;
            float rotateInfluence = SimulationRate * Time.deltaTime * RotationAmount * RotationScaleMultiplier;

            bool curHatLeft = OVRInput.Get(OVRInput.Button.PrimaryShoulder);

            if (curHatLeft && !prevHatLeft)
                euler.y -= RotationRatchet;

            prevHatLeft = curHatLeft;

            bool curHatRight = OVRInput.Get(OVRInput.Button.SecondaryShoulder);

            if (curHatRight && !prevHatRight)
                euler.y += RotationRatchet;

            prevHatRight = curHatRight;

            euler.y += buttonRotation;
            buttonRotation = 0f;

            if (SnapRotation)
            {
                if (OVRInput.Get(OVRInput.Button.SecondaryThumbstickLeft) ||
                    (RotationEitherThumbstick && OVRInput.Get(OVRInput.Button.PrimaryThumbstickLeft)))
                {
                    if (ReadyToSnapTurn)
                    {
                        euler.y -= RotationRatchet;
                        ReadyToSnapTurn = false;
                    }
                }
                else if (OVRInput.Get(OVRInput.Button.SecondaryThumbstickRight) ||
                         (RotationEitherThumbstick && OVRInput.Get(OVRInput.Button.PrimaryThumbstickRight)))
                {
                    if (ReadyToSnapTurn)
                    {
                        euler.y += RotationRatchet;
                        ReadyToSnapTurn = false;
                    }
                }
                else
                {
                    ReadyToSnapTurn = true;
                }
            }
            else
            {
                Vector2 secondaryAxis = OVRInput.Get(OVRInput.Axis2D.SecondaryThumbstick);
                if (RotationEitherThumbstick)
                {
                    Vector2 altSecondaryAxis = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick);
                    if (secondaryAxis.sqrMagnitude < altSecondaryAxis.sqrMagnitude)
                    {
                        secondaryAxis = altSecondaryAxis;
                    }
                }

                euler.y += secondaryAxis.x * rotateInfluence;
            }

            if (RotateAroundGuardianCenter)
            {
                //transform.rotation = Quaternion.Euler(euler);
                Rb.MoveRotation(Quaternion.Euler(euler));
            }
            else
            {
                transform.RotateAround(CameraRig.centerEyeAnchor.position, Vector3.up, euler.y);
            }
        }
#endif
    }

    /// <summary>
    /// Jump! Must be enabled manually.
    /// </summary>
    public bool Jump()
    {
        if (!isGrounded)
            return false;

        MoveThrottle += new Vector3(0, transform.lossyScale.y * JumpForce, 0);

        return true;
    }

    /// <summary>
    /// Stop this instance.
    /// </summary>
    public void Stop()
    {
        Rb.Move(Vector3.zero, Quaternion.identity);
        MoveThrottle = Vector3.zero;
    }

    private void OnCollisionStay(Collision collision)
    {
        if(collision.gameObject.layer == 10)
        {
            isGrounded = true;
        }
    }
    private void OnCollisionExit(Collision collision)
    {
        if (collision.gameObject.layer == 10)
        {
            isGrounded = false;
        }
    }

    /// <summary>
    /// Resets the player look rotation when the device orientation is reset.
    /// </summary>
    public void ResetOrientation()
    {
        if (HmdResetsY && !HmdRotatesY)
        {
            Vector3 euler = transform.rotation.eulerAngles;
            euler.y = InitialYRotation;
            transform.rotation = Quaternion.Euler(euler);
        }
    }
}

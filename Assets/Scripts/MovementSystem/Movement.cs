using System;
using UnityEngine;
using UnityEngine.InputSystem;

public class Movement : MonoBehaviour
{
    //Assignables #===============================#
    public LayerMask whatIsGround;
    public float maxSlopeAngle = 35f;
    public float runSpeed = 650f;
    public float walkSpeed = 200f;
    public float sensitivity = 50.0f;
    public Transform playerCam;
    public Transform orientation;
    public float maxSpeed = 450f;
    public bool enableHeadBobbing = true;
    public Camera camera; //Use actual Main Camera

    //Privates
    private bool cancellingGrounded;
    private float desiredX;
    private float sensMultiplier = 1f;
    private float xRotation;
    private Vector3 direction;

    //Head Bobbing
    private float runBobSpeed = 12f;
    [Range(0.01f, 0.1f)] public float runBobAmount = 0.05f;
    private float defaultYPos = 0;
    private float timer;

    //Performed inputs
    private Rigidbody rb;
    private PlayerInput playerInput;
    private MovementSystem movementSystem;

    //Bools
    public bool jumping, crouching;
    public bool isSprinting = false;
    public bool grounded;

    //Jumping
    private bool readyToJump = false;
    public float jumpForce = 450f;

    //Crouch
    private Vector3 crouchScale = new Vector3(1, 0.5f, 1);
    private Vector3 playerScale;

    //Sliding
    public float slideForce = 3000;
    public float slideCounterMovement = 0.2f;
    private Vector3 normalVector = Vector3.up;
    private Vector3 wallNormalVector;
    private float counterMovement = 0.225f;
    private float threshold = 0.03f;

    //Wallrunning
    private float wallRunCameraTilt = 0f;
    public LayerMask whatIsWall;
    public float wallrunForce = 3500;
    public float maxWallSpeed = 7;
    bool isWallRight, isWallLeft;
    public bool isWallRunning;
    private bool wallrunButtonHeld = false;
    public float maxWallRunCameraTilt = 15;

    //Animations
    public Animator animator;
    public bool allowRunAnimation = false;



    //Application methods #=======================#
    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        playerInput = GetComponent<PlayerInput>();

        //Call methods on input performed
        movementSystem = new MovementSystem();
        movementSystem.Movement.Enable();

        //Jumping and Double Jumping
        movementSystem.Movement.Jump.performed += Jump;

        //Crouching & Sliding
        movementSystem.Movement.Crouch.performed += StartCrouch;
        movementSystem.Movement.Crouch.canceled += StopCrouch;

        //Wallrunning
        movementSystem.Movement.Wallrun.performed += ctx => wallrunButtonHeld = true ;
        movementSystem.Movement.Wallrun.canceled += WallJump;

        //Head Bobbing
        defaultYPos = camera.transform.localPosition.y;

        //Sprinting
        movementSystem.Movement.Sprint.performed += context => isSprinting = true;
        movementSystem.Movement.Sprint.canceled += context => isSprinting = false;
    }

    // Start is called before the first frame update
    void Start()
    {
        playerScale = transform.localScale;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        Invoke(nameof(ResetJump), 1.0f);
        animator = GetComponent<Animator>();
    }

    // Update is called once per frame
    void Update()
    {
        Look();
        CheckForWall();

    }

    //FixedUpdate is for RigidBodies
    private void FixedUpdate()
    {
        if (!isWallRunning) Move();
        if (enableHeadBobbing) HeadBob();
        PlayRunAnimation();
        if ((isWallRight || isWallLeft) && wallrunButtonHeld) Wallrun();
    }


    private void PlayRunAnimation()
    {
        //if (allowRunAnimation)
        //{
        //    animator.SetBool("isRunning", true);
        //}

        //if (!allowRunAnimation)
        //{
        //    animator.SetBool("isRunning", false);
        //}
    }


    //Ground Detection #==========================#
    private bool IsFloor(Vector3 v)
    {
        float angle = Vector3.Angle(Vector3.up, v);
        return angle < maxSlopeAngle;
    }


    private void OnCollisionStay(Collision other)
    {
        //Make sure we are only checking for walkable layers
        int layer = other.gameObject.layer;
        if (whatIsGround != (whatIsGround | (1 << layer))) return;

        //Iterate through every collision in a physics update
        for (int i = 0; i < other.contactCount; i++)
        {
            Vector3 normal = other.contacts[i].normal;
            if (IsFloor(normal))
            {
                grounded = true;
                cancellingGrounded = false;
                normalVector = normal;
                ResetJump();
                CancelInvoke(nameof(StopGrounded));
            }
        }

        float delay = 3f;
        if (!cancellingGrounded)
        {
            cancellingGrounded = true;
            Invoke(nameof(StopGrounded), Time.deltaTime * delay);
        }
    }

    private void StopGrounded()
    {
        grounded = false;
    }


    //Movement Functions #========================#
    public void Move()
    {
        //Extra gravity
        rb.AddForce(Vector3.down * Time.fixedDeltaTime * 10);

        //Detect movement input
        Vector2 inputVector = movementSystem.Movement.Walk.ReadValue<Vector2>();
        

        //Find actual velocity relative to where player is looking
        Vector2 mag = FindVelRelativeToLook();
        float xMag = mag.x, yMag = mag.y;

        //Counteract sliding and sloppy movement
        CounterMovement(inputVector.x, inputVector.y, mag);

        //If speed is larger than maxspeed, cancel out the input so you don't go over max speed
        if (inputVector.x > 0 && xMag > maxSpeed) inputVector.x = 0;
        if (inputVector.x < 0 && xMag < -maxSpeed) inputVector.x = 0;
        if (inputVector.y > 0 && yMag > maxSpeed) inputVector.y = 0;
        if (inputVector.y < 0 && yMag < -maxSpeed) inputVector.y = 0;

        float multiplier = 1f;

        // Movement while mid-air
        if (!grounded && !isWallRunning)
        {
            multiplier = 0.1f;
        }


        // Disable movement while sliding
        if (grounded && crouching) multiplier = 0f;

        // Check for Sprint input
        float speed = walkSpeed;
        if (isSprinting)
        {
            speed = runSpeed;
        }


        //Apply forces to move player
        direction = new Vector3(inputVector.x, 0, inputVector.y);
        direction = orientation.transform.TransformDirection(direction);
        rb.AddForce(direction * speed * Time.fixedDeltaTime * multiplier, ForceMode.Force);

    }

    private void CounterMovement(float x, float y, Vector2 mag)
    {
        if (!grounded || jumping) return;

        //Slow down sliding
        if (crouching)
        {
            rb.AddForce(runSpeed * Time.deltaTime * -rb.velocity.normalized * slideCounterMovement);
            return;
        }

        //Counter movement
        if (Math.Abs(mag.x) > threshold && Math.Abs(x) < 0.05f || (mag.x < -threshold && x > 0) || (mag.x > threshold && x < 0))
        {
            rb.AddForce(runSpeed * orientation.transform.right * Time.deltaTime * -mag.x * counterMovement);
        }
        if (Math.Abs(mag.y) > threshold && Math.Abs(y) < 0.05f || (mag.y < -threshold && y > 0) || (mag.y > threshold && y < 0))
        {
            rb.AddForce(runSpeed * orientation.transform.forward * Time.deltaTime * -mag.y * counterMovement);
        }

        //Limit diagonal running. This will also cause a full stop if sliding fast and un-crouching, so not optimal.
        if (Mathf.Sqrt((Mathf.Pow(rb.velocity.x, 2) + Mathf.Pow(rb.velocity.z, 2))) > maxSpeed)
        {
            float fallspeed = rb.velocity.y;
            Vector3 n = rb.velocity.normalized * maxSpeed;
            rb.velocity = new Vector3(n.x, fallspeed, n.z);
        }

    }

    public void Jump(InputAction.CallbackContext context)
    {
        if (grounded && readyToJump && !isWallRunning && !crouching)
        {
            Vector3 jump = new Vector3(0, jumpForce * 0.75f, 0);

            //Add jump forces
            rb.AddForce(jump * Time.fixedDeltaTime, ForceMode.Impulse);

            readyToJump = false;

            Invoke(nameof(ResetJump), 0.2f);
        }

    }


    private void ResetJump()
    {
        readyToJump = true;
    }


    public void StartCrouch(InputAction.CallbackContext context)
    {
        transform.localScale = crouchScale;
        Vector3 crouchPosition = new Vector3(0, 0.5f, 0);
        transform.position = Vector3.Lerp(transform.position, crouchPosition, Time.fixedDeltaTime);
        crouching = true;

        //Sliding
        if (rb.velocity.magnitude > 1f && grounded)
        {
            rb.AddForce(orientation.transform.forward * slideForce);
        } 
    }

    public void StopCrouch(InputAction.CallbackContext context)
    {
        transform.localScale = playerScale;
        Vector3 standingPosition = new Vector3(0, 1f, 0);
        transform.position = Vector3.Lerp(transform.position, standingPosition, Time.fixedDeltaTime);
        crouching = false;
    }


    private void Look()
    {
        float mouseX = Input.GetAxis("Mouse X") * sensitivity * Time.fixedDeltaTime * sensMultiplier;
        float mouseY = Input.GetAxis("Mouse Y") * sensitivity * Time.fixedDeltaTime * sensMultiplier;

        //Find current look rotation
        Vector3 rot = playerCam.transform.localRotation.eulerAngles;
        desiredX = rot.y + mouseX;

        //Rotate, and also make sure we dont over- or under-rotate.
        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, -90f, 65f);

        //Perform the rotations
        playerCam.transform.localRotation = Quaternion.Euler(xRotation, desiredX, wallRunCameraTilt);
        orientation.transform.localRotation = Quaternion.Euler(0, desiredX, 0);

        //Wallrunning camera tilt
        if (Math.Abs(wallRunCameraTilt) < maxWallRunCameraTilt && isWallRunning && isWallRight)
            wallRunCameraTilt += Time.deltaTime * maxWallRunCameraTilt * 5;
        if (Math.Abs(wallRunCameraTilt) < maxWallRunCameraTilt && isWallRunning && isWallLeft)
            wallRunCameraTilt -= Time.deltaTime * maxWallRunCameraTilt * 5;

        //Tilts camera back again
        if (wallRunCameraTilt > 0 && !isWallRight && !isWallLeft)
            wallRunCameraTilt -= Time.deltaTime * maxWallRunCameraTilt * 2;
        if (wallRunCameraTilt < 0 && !isWallRight && !isWallLeft)
            wallRunCameraTilt += Time.deltaTime * maxWallRunCameraTilt * 2;
    }

    private void HeadBob()
    {
        if (!grounded || crouching)
        {
            allowRunAnimation = false;
            return;
        }

        if (rb.velocity.magnitude > 0.5f)
        {
            timer += Time.deltaTime * runBobSpeed;
            camera.transform.localPosition = new Vector3(camera.transform.localPosition.x, defaultYPos + Mathf.Sin(timer) * runBobAmount, camera.transform.localPosition.z);
            allowRunAnimation = true;
        } else
        {
            allowRunAnimation = false;
        }
    }

    public Vector2 FindVelRelativeToLook()
    {
        float lookAngle = orientation.transform.eulerAngles.y;
        float moveAngle = Mathf.Atan2(rb.velocity.x, rb.velocity.z) * Mathf.Rad2Deg;

        float u = Mathf.DeltaAngle(lookAngle, moveAngle);
        float v = 90 - u;

        float magnitue = rb.velocity.magnitude;
        float yMag = magnitue * Mathf.Cos(u * Mathf.Deg2Rad);
        float xMag = magnitue * Mathf.Cos(v * Mathf.Deg2Rad);

        return new Vector2(xMag, yMag);
    }




    //Wallrunning component #========================================#
    private void Wallrun()
    {
        if (!grounded)
        {
            rb.useGravity = false;
            isWallRunning = true;

            if (rb.velocity.magnitude <= maxWallSpeed)
            {
                //Negate upward motion, but keep forward momentum
                Vector3 momentum = new Vector3(orientation.forward.x, 0, orientation.forward.z);
                rb.AddForce(momentum * wallrunForce * Time.fixedDeltaTime);

                if (isWallRight)
                    rb.AddForce(orientation.right * wallrunForce / 7 * Time.fixedDeltaTime);
                else
                    rb.AddForce(-orientation.right * wallrunForce / 7 * Time.fixedDeltaTime);
            }
        } 
    }
    private void StopWallRun()
    {
        isWallRunning = false;
        rb.useGravity = true;
    }
    private void CheckForWall()
    {
        isWallRight = Physics.Raycast(transform.position, orientation.right, 1f, whatIsWall);
        isWallLeft = Physics.Raycast(transform.position, -orientation.right, 1f, whatIsWall);

        if (!isWallLeft && !isWallRight) StopWallRun();
    }

    private void WallJump(InputAction.CallbackContext context)
    {
        wallrunButtonHeld = false;

        if (isWallRunning)
        {
            readyToJump = false;
            Vector3 jump = new Vector3(0, jumpForce * 0.5f, 0);

            //Leap forwards when jumping off a wall
            rb.AddForce(orientation.forward * (jumpForce * 0.7f) * Time.fixedDeltaTime, ForceMode.Impulse);
            rb.AddForce(jump * Time.fixedDeltaTime, ForceMode.Impulse);

            if (isWallLeft)
            {
                rb.AddForce(orientation.right * (jumpForce * 0.3f) * Time.fixedDeltaTime, ForceMode.Impulse);
            }
            else
            {
                rb.AddForce(-orientation.right * (jumpForce * 0.3f) * Time.fixedDeltaTime, ForceMode.Impulse);
            }

        }
    }
}

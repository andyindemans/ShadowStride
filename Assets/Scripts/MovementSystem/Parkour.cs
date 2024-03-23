using System;
using UnityEngine;
using UnityEngine.InputSystem;

public class Parkour : MonoBehaviour
{
    //Assignables #===============================#
    public float vaultSpeed = 5f;
    public LayerMask whatIsVaultable;
    public bool isWallVaultable;

    //Privates
    private Rigidbody rb;
    private PlayerInput playerInput;
    private MovementSystem movementSystem;
    private Movement movementParams;
    private float playerHeight;
    private Transform orientation;

    //Bools
    private bool grounded;
    private bool parkourButtonHeld;
    private bool isVaulting = false;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        playerInput = GetComponent<PlayerInput>();

        //Call methods on input performed
        movementSystem = new MovementSystem();
        movementSystem.Movement.Enable();

        //Parkour
        movementSystem.Movement.Parkour.performed += context => parkourButtonHeld = true;
        movementSystem.Movement.Parkour.canceled += context => parkourButtonHeld = false;
    }

    // Start is called before the first frame update
    void Start()
    {
        //Load Movement component
        movementParams = GetComponent<Movement>();

        //Get size of RigidBody
        playerHeight = rb.GetComponent<Renderer>().bounds.size.y * 2.5f;
    }

    // Update is called once per frame
    void Update()
    {
        CheckForWall();
        if (parkourButtonHeld) Vault();
    }

    //Used for RigidBodies
    void FixedUpdate()
    {
        grounded = movementParams.grounded;
        orientation = movementParams.orientation;
        if (!grounded && isVaulting)
        {
            Invoke(nameof(ResetCrouch), 1f);
            isVaulting = false;
        }
    }


    //Parkour moves #=============================#
    private void Vault()
    {
        if (grounded && isWallVaultable)
        {
            Vector3 vault = new Vector3(orientation.forward.x, playerHeight * 1.5f, 0);

            //Vault over object
            rb.MovePosition( transform.position + vault * Time.fixedDeltaTime * vaultSpeed);
            movementSystem.Movement.Crouch.performed += movementParams.StartCrouch;

            isVaulting = true;
        }

    }

    private void ResetCrouch()
    {
        movementSystem.Movement.Crouch.canceled += movementParams.StopCrouch;
    }

    private void CheckForWall()
    {
        Vector3 rayHeight = new Vector3(transform.position.x, transform.position.y - 0.7f, transform.position.z);
        isWallVaultable = Physics.Raycast(rayHeight, orientation.forward, 0.75f, whatIsVaultable);

    }
}

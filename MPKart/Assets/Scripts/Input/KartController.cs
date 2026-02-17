using System;
using UnityEditor.Rendering;
using UnityEngine;

namespace Kart
{
    [System.Serializable ]
    public class AxleInfo
    {
        public WheelCollider leftWheel;
        public WheelCollider rightWheel;
        public bool motor;
        public bool steering;
        public WheelFrictionCurve originalForwardFriction;
        public WheelFrictionCurve originalSidewaysFriction;
    }

    public class KartController : MonoBehaviour
    {
        [Header("Axle Information")]
        [SerializeField] AxleInfo[] axleInfos;

        [Header("Motor Attributes")]
        [SerializeField] float maxMotorTorque = 3000f;
        [SerializeField] float maxSpeed;

        [Header("Steering Attributes")]
        [SerializeField] float maxSteeringAngle = 30f;
        [SerializeField] AnimationCurve turnCurve;
        [SerializeField] float turnStrenght = 1500f;

        [Header("Braking and Drifting Attributes")]
        [SerializeField] float driftSteerMultiplier = 1.5f;
        [SerializeField] float brakeTorque = 10000f;

        [Header("Physics")]
        [SerializeField] Transform centerOfMass;
        [SerializeField] float downForce = 100f;
        [SerializeField] float gravity = Physics.gravity.y;
        [SerializeField] float lateralGScale = 10f;

        [Header("Banking")]
        [SerializeField] float maxBankAngle = 5f;
        [SerializeField] float bankSpeed = 2f;

        [Header("Refs")]
        [SerializeField] InputReader input;
        Rigidbody rb;

        Vector3 kartVelocity;
        float brakeVelocity;
        float driftVelocity;

        RaycastHit hit;

        const float thresholdSpeed = 10f;
        const float centerOfMassOffset = -0.5f;
        Vector3 originalCenterOfMass;

        public bool IsGrounded = true;
        public Vector3 Velocity => kartVelocity;
        public float MaxSpeed => maxSpeed;

        private void Start()
        {
            rb = GetComponent<Rigidbody>();
            input.Enable();

            rb.centerOfMass = centerOfMass.localPosition;
            originalCenterOfMass = centerOfMass.localPosition;

            foreach (AxleInfo axleInfo in axleInfos) {
                axleInfo.originalForwardFriction = axleInfo.leftWheel.forwardFriction;
                axleInfo.originalSidewaysFriction = axleInfo.leftWheel.sidewaysFriction;

            }
        }

        private void FixedUpdate()
        {
            float verticalInput = AdjustInput(input.Move.y);
            float horizontalInput = AdjustInput(input.Move.x);

            float motor = maxMotorTorque * verticalInput;
            float steering = maxSteeringAngle * horizontalInput;

            //Debug.Log("Motor: " + motor);
            //Debug.Log("Steering: " + steering);
            //Debug.Log("Vertical Input: " + verticalInput);
            //Debug.Log("Horizontal Input: " + horizontalInput);


            UpdateAxles(motor, steering);
            UpdateBanking(horizontalInput);

            kartVelocity = transform.InverseTransformDirection(rb.linearVelocity);//uses linearVelocity

            if (IsGrounded)
            {
                HandleGroundedMovement(verticalInput, horizontalInput);
            }
            else
            {
                HandleAirborneMovement(verticalInput, horizontalInput);
            }
        }

        private void HandleAirborneMovement(float verticalInput, float horizontalInput)
        {
            rb.linearVelocity = Vector3.Lerp(rb.linearVelocity, rb.linearVelocity + Vector3.down * gravity, Time.deltaTime * gravity);
        }

        private void HandleGroundedMovement(float verticalInput, float horizontalInput)
        {
            //turn logic
            if(Mathf.Abs(verticalInput) > 0.1f || Mathf.Abs(kartVelocity.z) > 1)
            {
                float turnMultiplier = Mathf.Clamp01(turnCurve.Evaluate(kartVelocity.magnitude / maxSpeed));
                rb.AddTorque(Vector3.up * horizontalInput * Mathf.Sign(kartVelocity.z) * turnStrenght * 100f * turnMultiplier);
            }

            //Acceleration logic
            if (!input.IsBraking) {
                float targetSpeed = verticalInput * maxSpeed;
                Vector3 forwardWithoutY = new Vector3(transform.forward.x,
                    0,
                    transform.forward.z).normalized;
                rb.linearVelocity = Vector3.Lerp(rb.linearVelocity, forwardWithoutY * targetSpeed, Time.deltaTime);              
            }

            //Downforce
            float speedFactor = Mathf.Clamp01(rb.linearVelocity.magnitude / maxSpeed);
            float lateralG = Mathf.Abs(Vector3.Dot(rb.linearVelocity, transform.right));
            float downforceFactor = Mathf.Max(speedFactor, lateralG / lateralGScale);
            rb.AddForce(-transform.up * downForce * rb.mass * downforceFactor);

            //Shift Center of Mass
            float speed = rb.linearVelocity.magnitude;
            Vector3 centerOfMassAdjustment = (speed > thresholdSpeed)
                ? new Vector3(0f,0f,Mathf.Abs(verticalInput) > 0.1f ?
                    Mathf.Sign(verticalInput) * centerOfMassOffset
                    : 0f)
                : Vector3.zero;

            rb.centerOfMass = originalCenterOfMass + centerOfMassAdjustment;
        }

        private void UpdateBanking(float horizontalInput)
        {
            float targetBankAngle = horizontalInput * -maxBankAngle;
            Vector3 currentEuler = transform.localEulerAngles;
            currentEuler.z = Mathf.LerpAngle(currentEuler.z, targetBankAngle, Time.deltaTime * bankSpeed);
            transform.localEulerAngles = currentEuler;
        }

        private void UpdateAxles(float motor, float steering)
        {
            foreach (AxleInfo axleInfo in axleInfos) { 
                HandleSteering(axleInfo, steering);
                HandleMotor(axleInfo, motor);
                HandleBrakesAndDrift(axleInfo);
                UpdateWheelVisuals(axleInfo.leftWheel);
                UpdateWheelVisuals(axleInfo.rightWheel);

            }
        }

        private void UpdateWheelVisuals(WheelCollider collider)
        {
            if (collider.transform.childCount == 0) return;

            Transform visualWheel = collider.transform.GetChild(0);

            Vector3 position;
            Quaternion rotation;
            collider.GetWorldPose(out position, out rotation);

            visualWheel.transform.position = position;
            visualWheel.transform.rotation = rotation;
        }

        private void HandleBrakesAndDrift(AxleInfo axleInfo)
        {
            if (axleInfo.motor)
            {
                if (input.IsBraking)
                {
                    rb.constraints = RigidbodyConstraints.FreezeRotationX;
                    float newZ = Mathf.SmoothDamp(rb.linearVelocity.z, 0, ref brakeVelocity, 1f);
                    rb.linearVelocity = new Vector3(rb.linearVelocity.x, rb.linearVelocity.y, newZ);

                    axleInfo.leftWheel.brakeTorque = brakeTorque;
                    axleInfo.rightWheel.brakeTorque = brakeTorque;
                }
                
            }
            else
            {
                rb.constraints = RigidbodyConstraints.None;

                axleInfo.leftWheel.brakeTorque = 0;
                axleInfo.rightWheel.brakeTorque = 0;
            }
        }

        private void HandleMotor(AxleInfo axleInfo, float motor)
        {
            if (axleInfo.motor)
            {
                axleInfo.leftWheel.motorTorque = motor;
                axleInfo.rightWheel.motorTorque = motor;
            }
        }

        private void HandleSteering(AxleInfo axleInfo, float steering)
        {
            if (axleInfo.steering)
            {
                axleInfo.leftWheel.steerAngle = steering;
                axleInfo.rightWheel.steerAngle = steering;
            }
        }

        float AdjustInput(float input) {
            return input switch
            {
                >= .7f => 1f,
                <= -.7f => -1f,
                _ => input
            };
        }

    }
}

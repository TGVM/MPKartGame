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

        [Header("Braking and Drifting Attributes")]
        [SerializeField] float brakeTorque = 10000f;
        float brakeVelocity;


        [SerializeField] InputReader input;
        Rigidbody rb;

        private void Start()
        {
            rb = GetComponent<Rigidbody>();
            input.Enable();

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

            Debug.Log("Motor: " + motor);
            Debug.Log("Steering: " + steering);
            Debug.Log("Vertical Input: " + verticalInput);
            Debug.Log("Horizontal Input: " + horizontalInput);


            UpdateAxles(motor, steering);
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

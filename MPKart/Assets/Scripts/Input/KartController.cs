using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using Unity.Cinemachine;
using Unity.Netcode;
using UnityEngine;
using Utilities;

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

    public struct InputPayload : INetworkSerializable
    {
        public int tick;
        public DateTime timestamp;
        public ulong networkObjectId;
        public Vector3 inputVector;
        public Vector3 position;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref tick);
            serializer.SerializeValue(ref timestamp);
            serializer.SerializeValue(ref networkObjectId);
            serializer.SerializeValue(ref inputVector);
            serializer.SerializeValue(ref position);
        }
    }

    public struct StatePayload : INetworkSerializable
    {
        public int tick;
        public ulong networkObjectId;
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 velocity;
        public Vector3 angularVelocity;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref tick);
            serializer.SerializeValue(ref networkObjectId);
            serializer.SerializeValue(ref position);
            serializer.SerializeValue(ref rotation);
            serializer.SerializeValue(ref velocity);
            serializer.SerializeValue(ref angularVelocity);
        }
    }


    public class KartController : NetworkBehaviour
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
        [SerializeField] InputReader playerInput;
        [SerializeField] Circuit circuit;
        [SerializeField] AIDriverData driverData;
        [SerializeField] CinemachineCamera playerCamera;
        [SerializeField] AudioListener playerAudioListener;

        [Header("Netcode")]
        [SerializeField] float reconciliationCooldownTime = 1f;
        [SerializeField] float reconciliationThreshold = 10f;
        [SerializeField] float extrapolationLimit = 0.5f;   //500ms
        [SerializeField] float extrapolationMultiplier = 1.2f;
        [SerializeField] GameObject serverCube;
        [SerializeField] GameObject clientCube;

        StatePayload extrapolationState;
        CountdownTimer extrapolationCooldown;

        CountdownTimer reconciliationCooldown;

        [Header("NetcodeDebug")]
        [SerializeField] TextMeshProUGUI networkStatusText;
        [SerializeField] TextMeshProUGUI playerStatusText;
        [SerializeField] TextMeshProUGUI serverRpcDebugText;
        [SerializeField] TextMeshProUGUI clientRpcDebugText;


        IDrive input;
        Rigidbody rb;
        ClientNetworkTransform clientNetworkTransform;

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

        //Netcode general
        NetworkTimer timer;
        const float k_serverTickRate = 60f; //60fps
        const int k_bufferSize = 1024;

        //Netcode client specific
        CircularBuffer<StatePayload> clientStateBuffer;
        CircularBuffer<InputPayload> clientInputBuffer;
        StatePayload lastServerState;
        StatePayload lastProcessedState;

        //Netcode server specific
        CircularBuffer<StatePayload> serverStateBuffer;
        Queue<InputPayload> serverInputQueue;


        private void Awake()
        {
            if(playerInput is IDrive driveInput)
            {
                input = driveInput;
            }


            rb = GetComponent<Rigidbody>();
            clientNetworkTransform = GetComponent<ClientNetworkTransform>();
            input.Enable();

            rb.centerOfMass = centerOfMass.localPosition;
            originalCenterOfMass = centerOfMass.localPosition;

            foreach (AxleInfo axleInfo in axleInfos)
            {
                axleInfo.originalForwardFriction = axleInfo.leftWheel.forwardFriction;
                axleInfo.originalSidewaysFriction = axleInfo.leftWheel.sidewaysFriction;
            }

            timer = new NetworkTimer(k_serverTickRate);
            clientStateBuffer = new CircularBuffer<StatePayload>(k_bufferSize);
            clientInputBuffer = new CircularBuffer<InputPayload>(k_bufferSize);

            serverStateBuffer = new CircularBuffer<StatePayload>(k_bufferSize);
            serverInputQueue = new Queue<InputPayload>();

            reconciliationCooldown = new CountdownTimer(reconciliationCooldownTime);
            extrapolationCooldown = new CountdownTimer(extrapolationLimit);

            reconciliationCooldown.OnTimerStart += () =>
            {
                extrapolationCooldown.Stop();
            };

            extrapolationCooldown.OnTimerStart += () =>
            {
                reconciliationCooldown.Stop();
                SwitchAuthorityMode(ClientNetworkTransform.AuthorityMode.Server); 
            };

            extrapolationCooldown.OnTimerStop += () =>
            {
                extrapolationState = default;
                SwitchAuthorityMode(ClientNetworkTransform.AuthorityMode.Client);
            };
        }

        void SwitchAuthorityMode(ClientNetworkTransform.AuthorityMode mode)
        {
            clientNetworkTransform.authorityMode = mode;
            bool shouldSync = mode == ClientNetworkTransform.AuthorityMode.Client;
            clientNetworkTransform.SyncPositionX = false;
            clientNetworkTransform.SyncPositionY = false;
            clientNetworkTransform.SyncPositionZ = false;
        }

        public void SetInput(IDrive input)
        {
            this.input = input;
        }

        public override void OnNetworkSpawn()
        {
            if (!IsOwner)
            {
                playerAudioListener.enabled = false;
                playerCamera.Priority = 0;
                return;
            }

            networkStatusText.SetText($"Player {NetworkManager.LocalClientId} Host: {NetworkManager.IsHost} Server: {IsServer} Client: {IsClient}");
            if (!IsServer) serverRpcDebugText.SetText("Not Server");
            if (!IsClient) clientRpcDebugText.SetText("Not Client");

            playerCamera.Priority = 100;
            playerAudioListener.enabled = true;
        }

        private void Update()
        {
            timer.Update(Time.deltaTime);
            reconciliationCooldown.Tick(Time.deltaTime);
            extrapolationCooldown.Tick(Time.deltaTime);

            playerStatusText.SetText($"Owner: {IsOwner} Network ObjectId: {NetworkObjectId} Velocity: {kartVelocity.magnitude:F1}");

            Extrapolate();
        }

        private void FixedUpdate()
        {

            while (timer.ShouldTick()) {
                HandleClientTick();
                HandleServerTick();
            }

            Extrapolate();
        }

        private void HandleServerTick()
        {
            if (!IsServer) return;

            var bufferIndex = -1;
            InputPayload inputPayload = default;
            while (serverInputQueue.Count > 0)
            {
                inputPayload = serverInputQueue.Dequeue();

                bufferIndex = inputPayload.tick % k_bufferSize;

                StatePayload statePayload = ProcessMovement(inputPayload);
                serverCube.transform.position = new Vector3(
                    statePayload.position.x,
                    4,
                    statePayload.position.z);
                serverStateBuffer.Add(statePayload, bufferIndex);
            }

            if (bufferIndex == -1) return;
            SendToClientRpc(serverStateBuffer.Get(bufferIndex));
            HandleExtrapolation(serverStateBuffer.Get(bufferIndex), CalculateLatencyInMillis(inputPayload));
        }

        void Extrapolate()
        {
            if(IsServer && extrapolationCooldown.IsRunning)
            {
                transform.position += new Vector3(extrapolationState.position.x,
                    0,
                    extrapolationState.position.z);
            }
        }

        private void HandleExtrapolation(StatePayload latestState, float latency)
        {
            if(ShouldExtrapolate(latency))
            {
                float axisLenght = latency * latestState.angularVelocity.magnitude * Mathf.Rad2Deg;
                Quaternion angularRotation = Quaternion.AngleAxis(axisLenght, latestState.angularVelocity);
                if(extrapolationState.position != default)
                {
                    latestState = extrapolationState;
                }

                var posAdjustment = latestState.velocity * (1+latency*extrapolationMultiplier);
                extrapolationState.position = posAdjustment;
                extrapolationState.rotation = angularRotation * latestState.rotation;
                extrapolationState.velocity = latestState.velocity;
                extrapolationState.angularVelocity = latestState.angularVelocity;
                extrapolationCooldown.Start();
            }
            else
            {
                extrapolationCooldown.Stop();

            }
        }

        bool ShouldExtrapolate(float latency) => latency < extrapolationLimit && latency > Time.fixedDeltaTime;

        /*private StatePayload SimulateMovement(InputPayload inputPayload)
        {
            Physics.simulationMode = SimulationMode.Script;

            Move(inputPayload.inputVector);
            Physics.Simulate(Time.fixedDeltaTime);
            Physics.Simulate(Time.fixedDeltaTime);
            Physics.simulationMode = SimulationMode.FixedUpdate;

            return new StatePayload()
            {
                tick = inputPayload.tick,
                position = transform.position,
                rotation = transform.rotation,
                velocity = rb.linearVelocity,
                angularVelocity = rb.angularVelocity
            };
        }*/

        static float CalculateLatencyInMillis(InputPayload input)
        {
            return (DateTime.Now - input.timestamp).Milliseconds / 1000f;
        }


        [ClientRpc]
        private void SendToClientRpc(StatePayload statePayload)
        {
            clientRpcDebugText.SetText($"Received state from server Tick {statePayload.tick} Server POS: {statePayload.position}");

            if (!IsOwner) return;

            lastServerState = statePayload;
        }

        private void HandleClientTick()
        {
            if (!IsClient || !IsOwner) return;

            var currentTick = timer.CurrentTick;
            var bufferIndex = currentTick % k_bufferSize;

            InputPayload inputPayload = new InputPayload()
            {
                tick = currentTick,
                timestamp = DateTime.Now,
                networkObjectId = NetworkObjectId,
                inputVector = input.Move,
                position = transform.position
            };

            clientInputBuffer.Add(inputPayload, bufferIndex);
            SendToServerRpc(inputPayload);

            StatePayload statePayload = ProcessMovement(inputPayload);
            clientCube.transform.position = new Vector3(
                statePayload.position.x,
                4,
                statePayload.position.z); 
            clientStateBuffer.Add(statePayload, bufferIndex);

            HandleServerReconciliation();
        }

        private void HandleServerReconciliation()
        {
            if (!ShouldReconcile()) return;

            float positionError;
            int bufferIndex;
            StatePayload rewindState = default;

            bufferIndex = lastServerState.tick % k_bufferSize;
            if (bufferIndex - 1 < 0) return; //not enough info to reconcile

            rewindState = IsHost ?
                serverStateBuffer.Get(bufferIndex - 1) :
                lastServerState; //host rpcs execute immediatelly, so we can use the last server state
            positionError = Vector3.Distance(rewindState.position, clientStateBuffer.Get(bufferIndex).position);


            if(positionError > reconciliationThreshold)
            {
                ReconcileState(rewindState);
                reconciliationCooldown.Start();
            }

            lastProcessedState = rewindState;

        }

        private void ReconcileState(StatePayload rewindState)
        {
            transform.position = rewindState.position;
            transform.rotation = rewindState.rotation;
            rb.linearVelocity = rewindState.velocity;
            rb.angularVelocity = rewindState.angularVelocity;

            if (!rewindState.Equals(lastServerState)) return;

            clientStateBuffer.Add(rewindState, rewindState.tick);

            //replay all inputs front the rewind state to the current state
            int tickToReplay = lastServerState.tick;

            while (tickToReplay < timer.CurrentTick) { 
                int bufferIndex = tickToReplay % k_bufferSize;
                StatePayload statePayload = ProcessMovement(clientInputBuffer.Get(bufferIndex));
                clientStateBuffer.Add(statePayload, bufferIndex);
                tickToReplay++;
            }
        }

        private bool ShouldReconcile()
        {
            bool isNewServerState = !lastServerState.Equals(default);
            bool isLastStateUndefinedOrDifferent = lastProcessedState
                .Equals(default) || !lastProcessedState.Equals(lastServerState);

            return isNewServerState && isLastStateUndefinedOrDifferent
                && !reconciliationCooldown.IsRunning && !extrapolationCooldown.IsRunning;
        }

        [ServerRpc]
        private void SendToServerRpc(InputPayload inputPayload)
        {
            serverRpcDebugText.SetText($"Received input from client Tick: {inputPayload.tick} Client POS: {inputPayload.position}");
            serverInputQueue.Enqueue(inputPayload);
        }

        StatePayload ProcessMovement(InputPayload input)
        {
            Move(input.inputVector);

            return new StatePayload()
            {
                tick = input.tick,
                networkObjectId = input.networkObjectId,
                position = transform.position,
                rotation = transform.rotation,
                velocity = rb.linearVelocity,
                angularVelocity = rb.angularVelocity
            };
        }

        void Move(Vector2 inputVector)
        {
            float verticalInput = AdjustInput(input.Move.y);
            float horizontalInput = AdjustInput(input.Move.x);

            float motor = maxMotorTorque * verticalInput;
            float steering = maxSteeringAngle * horizontalInput;

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
                rb.linearVelocity = Vector3.Lerp(rb.linearVelocity, forwardWithoutY * targetSpeed, timer.MinTimeBetweenTicks);
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
                    ApplyDriftFriction(axleInfo.leftWheel);
                    ApplyDriftFriction(axleInfo.rightWheel);
                }
                else
                {
                    rb.constraints = RigidbodyConstraints.None;

                    axleInfo.leftWheel.brakeTorque = 0;
                    axleInfo.rightWheel.brakeTorque = 0;
                    ResetDriftFriction(axleInfo.leftWheel);
                    ResetDriftFriction(axleInfo.rightWheel);
                }
            }
        }

        private void ResetDriftFriction(WheelCollider wheel)
        {
            AxleInfo axleInfo = axleInfos.FirstOrDefault(axle => axle.leftWheel == wheel || axle.rightWheel == wheel);
            if (axleInfo == null) return;

            wheel.forwardFriction = axleInfo.originalForwardFriction;
            wheel.sidewaysFriction = axleInfo.originalSidewaysFriction;

        }

        private void ApplyDriftFriction(WheelCollider wheel)
        {
            if(wheel.GetGroundHit(out var hit))
            {
                wheel.forwardFriction = UpdateFriction(wheel.forwardFriction);
                wheel.sidewaysFriction = UpdateFriction(wheel.sidewaysFriction);
                IsGrounded = true;
            }
        }

        private WheelFrictionCurve UpdateFriction(WheelFrictionCurve friction)
        {
            friction.stiffness = input.IsBraking ? Mathf.SmoothDamp(friction.stiffness,.5f, ref driftVelocity, Time.deltaTime * 2f) 
                : 1f;
            return friction;
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
                float steeringMultiplier = input.IsBraking ? driftSteerMultiplier : 1f;
                axleInfo.leftWheel.steerAngle = steering * steeringMultiplier;
                axleInfo.rightWheel.steerAngle = steering * steeringMultiplier;
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

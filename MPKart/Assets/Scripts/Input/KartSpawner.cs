using Unity.Cinemachine;
using Unity.VisualScripting;
using UnityEngine;

namespace Kart
{
    public class KartSpawner : MonoBehaviour {
        [SerializeField] Circuit circuit;
        [SerializeField] AIDriverData aiDriverData;
        [SerializeField] GameObject[] aiKartPrefabs;

        [SerializeField] GameObject playerKartPrefab;
        [SerializeField] CinemachineCamera playerCamera;

        private void Start()
        {
            var playerKart = Instantiate(playerKartPrefab, circuit.spawnPoints[0].position, circuit.spawnPoints[0].rotation);
            playerCamera.Follow = playerKart.transform;
            playerCamera.LookAt = playerKart.transform;

            //Spawn AI Karts
            for (int i = 1; i < circuit.spawnPoints.Length; i++) { 
                new AIKartBuilder(aiKartPrefabs[Random.Range(0, aiKartPrefabs.Length)])
                    .WithCircuit(circuit)
                    .WithDriverData(aiDriverData)
                    .WithSpawnPoint(circuit.spawnPoints[i])
                    .Build();
            }
        }
    }

    class AIKartBuilder
    {
        GameObject prefab;
        AIDriverData data;
        Circuit circuit;
        Transform spawnPoint;

        public AIKartBuilder(GameObject prefab)
        {
            this.prefab = prefab;
        }

        public AIKartBuilder WithDriverData(AIDriverData data)
        {
            this.data = data;
            return this;
        }

        public AIKartBuilder WithCircuit(Circuit circuit)
        {
            this.circuit = circuit;
            return this;
        }

        public AIKartBuilder WithSpawnPoint(Transform spawnPoint)
        {
            this.spawnPoint = spawnPoint;
            return this;
        }

        public GameObject Build()
        {
            var instance = Object.Instantiate(prefab, spawnPoint.position, spawnPoint.rotation);
            var aiInput = instance.GetOrAddComponent<AIInput>();
            aiInput.AddCircuit(circuit);
            aiInput.AddDriverData(data);
            instance.GetComponent<KartController>().SetInput(aiInput);

            return instance;
        }
    }
}

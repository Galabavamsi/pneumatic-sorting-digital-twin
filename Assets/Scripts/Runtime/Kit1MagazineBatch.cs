using UnityEngine;

namespace Kit1DigitalTwin
{
    public sealed class Kit1MagazineBatch : MonoBehaviour
    {
        private static readonly string[] WorkpieceNames =
        {
            "Component_042", "Component_030", "Component_044", "Component_032",
            "Component_041", "Component_037", "Component_040", "Component_031",
            "Component_045", "Component_036", "Component_039", "Component_033",
            "Component_046", "Component_035", "Component_043", "Component_034"
        };

        private const float SlotHeightMetres = 0.01f;

        private readonly Kit1SensorSimulation.WorkpieceMaterial[] materialSequence =
            new Kit1SensorSimulation.WorkpieceMaterial[WorkpieceNames.Length];
        private readonly MeshRenderer[] renderers = new MeshRenderer[WorkpieceNames.Length];
        private readonly Transform[] itemTransforms = new Transform[WorkpieceNames.Length];
        private readonly Vector3[] targetWorldPositions = new Vector3[WorkpieceNames.Length];
        private int activeIndex;
        private int preparedIndex = -1;
        private float dropSpeed;
        private bool currentHasLeftSlot;
        private bool settled = true;

        public int RemainingCount => Mathf.Max(
            0, WorkpieceNames.Length - activeIndex - (currentHasLeftSlot ? 1 : 0));
        public bool Empty => RemainingCount == 0;
        public bool IsSettled => settled;
        public bool WorkpieceAtFeedSlot => !currentHasLeftSlot || (preparedIndex >= 0 && settled);
        public Kit1SensorSimulation.WorkpieceMaterial CurrentMaterial => materialSequence[activeIndex];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<Kit1MagazineBatch>() == null)
            {
                new GameObject("Kit 1 Mixed Magazine").AddComponent<Kit1MagazineBatch>();
            }
        }

        private void Start()
        {
            GenerateRandomMaterials(activeIndex);
            for (int index = 0; index < WorkpieceNames.Length; index++)
            {
                GameObject item = GameObject.Find(WorkpieceNames[index]);
                if (item == null)
                {
                    Debug.LogError($"Mixed magazine could not find {WorkpieceNames[index]}.");
                    enabled = false;
                    return;
                }

                itemTransforms[index] = item.transform;
                targetWorldPositions[index] = item.transform.position;
                renderers[index] = item.GetComponent<MeshRenderer>();
                SetRendererColor(renderers[index], materialSequence[index]);
            }

            Debug.Log($"Mixed magazine loaded with {WorkpieceNames.Length} workpieces.");
        }

        private void Update()
        {
            if (settled || preparedIndex < 0)
            {
                return;
            }

            dropSpeed += Physics.gravity.magnitude * Time.deltaTime;
            bool allSettled = true;
            for (int index = preparedIndex; index < itemTransforms.Length; index++)
            {
                itemTransforms[index].position = Vector3.MoveTowards(
                    itemTransforms[index].position,
                    targetWorldPositions[index],
                    dropSpeed * Time.deltaTime);

                if (Vector3.Distance(itemTransforms[index].position, targetWorldPositions[index]) > 0.0001f)
                {
                    allSettled = false;
                }
            }

            settled = allSettled;
            if (settled)
            {
                Debug.Log("Next magazine workpiece reached the presence sensor.");
            }
        }

        public void NotifyCurrentWorkpieceFed()
        {
            if (currentHasLeftSlot)
            {
                return;
            }

            currentHasLeftSlot = true;
            preparedIndex = activeIndex + 1;
            if (preparedIndex >= WorkpieceNames.Length)
            {
                preparedIndex = -1;
                settled = true;
                Debug.Log("Magazine empty sensor ON while final workpiece is being sorted.");
                return;
            }

            for (int index = preparedIndex; index < itemTransforms.Length; index++)
            {
                targetWorldPositions[index] += Vector3.down * SlotHeightMetres;
            }

            dropSpeed = 0f;
            settled = false;
            Debug.Log("Magazine slot cleared; remaining stack released by gravity.");
        }

        public bool PromotePreparedWorkpiece(
            out string componentName,
            out Kit1SensorSimulation.WorkpieceMaterial material)
        {
            componentName = string.Empty;
            material = Kit1SensorSimulation.WorkpieceMaterial.Metal;
            if (preparedIndex < 0 || !settled)
            {
                return false;
            }

            activeIndex = preparedIndex;
            preparedIndex = -1;
            currentHasLeftSlot = false;
            componentName = WorkpieceNames[activeIndex];
            material = materialSequence[activeIndex];
            Debug.Log($"{componentName} is active. Remaining in magazine: {RemainingCount}.");
            return true;
        }

        public void RandomizeRemainingMaterials()
        {
            if (Empty)
            {
                return;
            }

            GenerateRandomMaterials(activeIndex);
            for (int index = activeIndex; index < renderers.Length; index++)
            {
                SetRendererColor(renderers[index], materialSequence[index]);
            }

            Debug.Log($"Randomized {RemainingCount} remaining magazine workpieces.");
        }

        private void GenerateRandomMaterials(int startIndex)
        {
            int metalCount = 0;
            int plasticCount = 0;
            for (int index = startIndex; index < materialSequence.Length; index++)
            {
                materialSequence[index] = Random.value < 0.5f
                    ? Kit1SensorSimulation.WorkpieceMaterial.Metal
                    : Kit1SensorSimulation.WorkpieceMaterial.Plastic;

                if (materialSequence[index] == Kit1SensorSimulation.WorkpieceMaterial.Metal)
                {
                    metalCount++;
                }
                else
                {
                    plasticCount++;
                }
            }

            int count = materialSequence.Length - startIndex;
            if (count >= 2 && metalCount == 0)
            {
                materialSequence[startIndex] = Kit1SensorSimulation.WorkpieceMaterial.Metal;
            }
            else if (count >= 2 && plasticCount == 0)
            {
                materialSequence[startIndex] = Kit1SensorSimulation.WorkpieceMaterial.Plastic;
            }
        }

        private static void SetRendererColor(
            MeshRenderer rendererComponent,
            Kit1SensorSimulation.WorkpieceMaterial material)
        {
            if (rendererComponent == null)
            {
                return;
            }

            Color color = material == Kit1SensorSimulation.WorkpieceMaterial.Metal
                ? new Color(0.15f, 0.72f, 1f, 1f)
                : new Color(1f, 0.58f, 0.12f, 1f);
            MaterialPropertyBlock block = new MaterialPropertyBlock();
            rendererComponent.GetPropertyBlock(block);
            block.SetColor(Shader.PropertyToID("_BaseColor"), color);
            rendererComponent.SetPropertyBlock(block);
        }
    }
}

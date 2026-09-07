using System.Linq;
using UnityEngine;

namespace Kit1DigitalTwin
{
    public sealed class Kit1ComponentRegistry : MonoBehaviour
    {
        public Transform[] Components { get; private set; }

        private void Awake()
        {
            Components = GetComponentsInChildren<MeshRenderer>(true)
                .Select(rendererComponent => rendererComponent.transform)
                .OrderBy(component => component.name)
                .ToArray();

            Debug.Log($"Kit 1 component registry loaded {Components.Length} mesh groups.");
        }
    }
}

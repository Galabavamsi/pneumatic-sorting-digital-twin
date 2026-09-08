using System.Linq;
using UnityEngine;

namespace Kit1DigitalTwin
{
    public class KitComponentRegistry : MonoBehaviour
    {
        [SerializeField] private string kitId = "Kit";

        public string KitId => kitId;
        public Transform[] Components { get; private set; }

        public void Configure(string identifier)
        {
            kitId = identifier;
        }

        protected virtual void Awake()
        {
            Components = GetComponentsInChildren<MeshRenderer>(true)
                .Select(rendererComponent => rendererComponent.transform)
                .OrderBy(component => component.name)
                .ToArray();

            Debug.Log($"{kitId} component registry loaded {Components.Length} mesh groups.");
        }
    }
}

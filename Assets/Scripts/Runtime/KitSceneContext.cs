using UnityEngine;

namespace Kit1DigitalTwin
{
    public static class KitSceneContext
    {
        public static bool IsKit1Scene => GameObject.Find("Kit1_SortingStation") != null;

        public static bool HasViewerKit =>
            IsKit1Scene || GameObject.Find("Kit2_StampingStation") != null;
    }
}

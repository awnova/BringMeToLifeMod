//====================[ Imports ]====================
using EFT.Interactive;
using KeepMeAlive.Helpers;
using UnityEngine;
using EFT;

namespace KeepMeAlive.Components
{
    //====================[ InteractableBuilder ]====================
    /// <summary>
    /// Builder used specifically for dynamically instantiating small scoped interactables, like MedPicker.
    /// Uses Primitive cube but doesn't manage lifecycle, letting the specific interactable handle its own updates via Throttled update loops.
    /// </summary>
    public class InteractableBuilder<T> where T : InteractableObject
    {
        //====================[ Builder Methods ]====================
        public static GameObject Build(string name, Vector3 position, Vector3 scale, Transform parent, Player player, bool debug)
        {
            if (debug)
            {
                RevivalDebugLog.LogDebug("InteractableBuilder<" + typeof(T) + "> created");
            }
            
            return CreateGameObject(name, position, scale, parent, debug);
        }

        private static GameObject CreateGameObject(string name, Vector3 pos, Vector3 scale, Transform parent, bool debug)
        {
            GameObject interactableObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            interactableObject.name = name;
            
            // Set parent to strictly inherit position/rotation.
            interactableObject.transform.SetParent(parent, false);
            interactableObject.transform.localPosition = pos;
            interactableObject.transform.localScale = scale;
            
            interactableObject.AddComponent<T>();
            
            foreach (var collider in interactableObject.GetComponents<Collider>())
            {
                collider.enabled = false;
            }
            
            MeshRenderer renderer = interactableObject.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.enabled = debug;
            }
            
            // Allow the component (like MedPicker) to enable itself on its own cycle if valid.
            interactableObject.SetActive(true);

            return interactableObject;
        }
    }
}
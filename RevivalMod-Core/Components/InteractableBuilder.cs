using EFT;
using EFT.Interactive;
using UnityEngine;

namespace RevivalMod.Components
{
    public class InteractableBuilder<T> where T : InteractableObject
    {

        private static string _name;
        private static Vector3 _position;
        private static Vector3 _scale;
        private static Transform _parent;
        private static Player _player;
        private static bool _debug;

        public static GameObject Build(string name, Vector3 position, Vector3 scale, Transform parent, Player player, bool debug)
        {
            _name = name;
            _position = position;
            _scale = scale;
            _parent = parent;
            _player = player;
            _debug = debug;

            if (_debug)
            {
                Plugin.LogSource.LogDebug("InteractableBuilder<" + typeof(T) + "> created");
            }
            return CreateGameObject();
        }

        private static GameObject CreateGameObject()
        {
            GameObject interactableObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            interactableObject.name = _name;
            interactableObject.transform.position = _position;
            interactableObject.transform.localScale = _scale;
            interactableObject.transform.SetParent(_parent, false);
            
            // Add the component and store the reference
            T component = interactableObject.AddComponent<T>();
            
            // If T is BodyInteractable, set the Revivee property
            if (component is BodyInteractable bodyInteractable)
            {
                bodyInteractable.Revivee = _player;
            }
            
            // Get other components with null checks
            BoxCollider collider = interactableObject.GetComponent<BoxCollider>();
            if (collider != null)
            {
                collider.enabled = false;
            }
            
            MeshRenderer renderer = interactableObject.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.enabled = _debug;
            }
            
            interactableObject.SetActive(true);

            return interactableObject;
        }
    }
}
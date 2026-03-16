//====================[ Imports ]====================
using EFT;
using EFT.Interactive;
using UnityEngine;

namespace KeepMeAlive.Components
{
    //====================[ InteractableBuilder ]====================
    /// <summary>
    /// Generic builder for light-weight interactable objects (e.g. MedPickerInteractable).
    /// BodyInteractable now uses its own <see cref="BodyInteractable.AttachToPlayer"/> factory.
    /// </summary>
    public class InteractableBuilder<T> where T : InteractableObject
    {
        //====================[ Fields ]====================
        private static string _name;
        private static Vector3 _position;
        private static Vector3 _scale;
        private static Transform _parent;
        private static bool _debug;

        //====================[ Builder Methods ]====================
        public static GameObject Build(string name, Vector3 position, Vector3 scale, Transform parent, Player player, bool debug)
        {
            _name = name;
            _position = position;
            _scale = scale;
            _parent = parent;
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
            interactableObject.transform.SetParent(_parent, false);
            interactableObject.transform.localPosition = _position;
            interactableObject.transform.localScale = _scale;
            
            interactableObject.AddComponent<T>();
            
            foreach (var collider in interactableObject.GetComponents<Collider>())
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
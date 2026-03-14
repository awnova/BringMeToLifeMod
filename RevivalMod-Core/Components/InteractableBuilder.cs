//====================[ Imports ]====================
using EFT;
using EFT.Interactive;
using UnityEngine;
using System.Collections.Generic;

namespace KeepMeAlive.Components
{
    //====================[ InteractableBuilder ]====================
    public class InteractableBuilder<T> where T : InteractableObject
    {
        //====================[ Fields ]====================
        private static string _name;
        private static Vector3 _position;
        private static Vector3 _scale;
        private static Transform _parent;
        private static Player _player;
        private static bool _debug;

        //====================[ Builder Methods ]====================
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
            interactableObject.transform.SetParent(_parent, false);
            interactableObject.transform.localPosition = _position;
            interactableObject.transform.localScale = _scale;
            
            // Add the component and store the reference
            T component = interactableObject.AddComponent<T>();
            
            // If T is BodyInteractable, set the Revivee property
            if (component is BodyInteractable bodyInteractable)
            {
                bodyInteractable.Revivee = _player;

                // Use a capsule close to the player's visual body and scale it by MEDICAL_RANGE.
                if (_player != null)
                {
                    BuildPlayerShapedCollider(interactableObject, _player, _scale.x);
                    interactableObject.transform.localScale = Vector3.one;
                }
            }
            
            // Get other components with null checks
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

        private static void BuildPlayerShapedCollider(GameObject interactableObject, Player player, float scaleMultiplier)
        {
            var box = interactableObject.GetComponent<BoxCollider>();
            if (box != null)
            {
                Object.Destroy(box);
            }

            var capsule = interactableObject.AddComponent<CapsuleCollider>();
            capsule.direction = 1; // Y-axis

            if (!TryGetVisualBoundsInLocal(interactableObject.transform, player, out var localBounds))
            {
                // Fallback dimensions keep behavior predictable if bounds probing fails.
                float safe = Mathf.Max(0.1f, scaleMultiplier);
                capsule.center = Vector3.zero;
                capsule.radius = 0.35f * safe;
                capsule.height = 1.8f * safe;
                return;
            }

            float multiplier = Mathf.Max(0.1f, scaleMultiplier);
            capsule.center = localBounds.center;
            capsule.radius = Mathf.Max(0.1f, Mathf.Max(localBounds.extents.x, localBounds.extents.z) * multiplier);
            capsule.height = Mathf.Max(capsule.radius * 2f + 0.01f, localBounds.size.y * multiplier);
        }

        private static bool TryGetVisualBoundsInLocal(Transform targetSpace, Player player, out Bounds localBounds)
        {
            localBounds = default;

            var renderers = player.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0)
            {
                return false;
            }

            bool initialized = false;
            foreach (var renderer in renderers)
            {
                if (renderer == null || !renderer.enabled)
                {
                    continue;
                }

                var world = renderer.bounds;
                foreach (var corner in GetBoundsCorners(world))
                {
                    var localPoint = targetSpace.InverseTransformPoint(corner);
                    if (!initialized)
                    {
                        localBounds = new Bounds(localPoint, Vector3.zero);
                        initialized = true;
                    }
                    else
                    {
                        localBounds.Encapsulate(localPoint);
                    }
                }
            }

            return initialized;
        }

        private static IEnumerable<Vector3> GetBoundsCorners(Bounds b)
        {
            var min = b.min;
            var max = b.max;

            yield return new Vector3(min.x, min.y, min.z);
            yield return new Vector3(min.x, min.y, max.z);
            yield return new Vector3(min.x, max.y, min.z);
            yield return new Vector3(min.x, max.y, max.z);
            yield return new Vector3(max.x, min.y, min.z);
            yield return new Vector3(max.x, min.y, max.z);
            yield return new Vector3(max.x, max.y, min.z);
            yield return new Vector3(max.x, max.y, max.z);
        }
    }
}
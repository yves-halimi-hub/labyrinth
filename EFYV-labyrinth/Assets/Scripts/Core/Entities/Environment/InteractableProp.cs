using UnityEngine;

namespace EFYV.Core.Entities.Environment
{
    // E.g. Chests (Blocking), XP Gems / Coins / Powerups (Non-Blocking)
    public abstract class InteractableProp : PropEntity
    {
        // Called when the player physically touches the prop
        public abstract void OnInteract(PlayerController player);

        // Uses Unity's highly optimized internal Physics callback
        protected virtual void OnTriggerEnter2D(Collider2D collision)
        {
            // PERFORMANCE: Direct Memory Reference Equality Check (O(1))
            // Bypasses the heavy C++ string-matching GetComponent<PlayerController>() overhead.
            if (PlayerController.Instance != null && collision.gameObject == PlayerController.Instance.gameObject)
            {
                OnInteract(PlayerController.Instance);
            }
        }
        
        // For solid interactables (like Chests), they might use OnCollisionEnter2D instead of Trigger
        protected virtual void OnCollisionEnter2D(Collision2D collision)
        {
            if (PlayerController.Instance != null && collision.gameObject == PlayerController.Instance.gameObject)
            {
                OnInteract(PlayerController.Instance);
            }
        }
    }
}

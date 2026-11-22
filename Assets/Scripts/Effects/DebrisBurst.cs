using UnityEngine;

namespace Shooter.Effects
{
    /// <summary>
    /// Throwaway physics debris for bullet sparks and enemy deaths. Pieces use built-in
    /// primitives and a shared material, are placed on the Ignore Raycast layer (so they
    /// can never block the player's shots), and destroy themselves after a short lifetime.
    /// </summary>
    public static class DebrisBurst
    {
        public static void Spawn(
            Vector3 position,
            Vector3 direction,
            Material material,
            int count,
            float force,
            float size,
            float lifetime)
        {
            if (material == null || count <= 0)
            {
                return;
            }

            Vector3 push = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.up;

            for (int i = 0; i < count; i++)
            {
                GameObject piece = FoundryEffects.Spawn(FoundryEffect.Debris, position, Quaternion.identity,
                    1f, lifetime) ?? GameObject.CreatePrimitive(PrimitiveType.Cube);
                piece.name = "Debris";
                piece.layer = 2; // Ignore Raycast: sparks must not eat incoming bullets.

                Collider collider = piece.GetComponent<Collider>();

                if (collider != null)
                {
                    Object.Destroy(collider);
                }

                piece.transform.position = position + Random.insideUnitSphere * 0.05f;
                piece.transform.rotation = Random.rotation;
                float meshScale = 1f;
                piece.transform.localScale = Vector3.one * (meshScale * size * Random.Range(0.6f, 1.4f));

                MeshRenderer renderer = piece.GetComponent<MeshRenderer>();

                if (renderer != null)
                {
                    renderer.sharedMaterial = material;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }

                Rigidbody body = piece.GetComponent<Rigidbody>();

                if (body == null)
                {
                    body = piece.AddComponent<Rigidbody>();
                }
                body.mass = 0.05f;
                body.linearDamping = 0.1f;
                body.angularDamping = 0.05f;

                Vector3 velocity = (push + Random.insideUnitSphere * 0.9f).normalized * force * Random.Range(0.7f, 1.3f);
                body.AddForce(velocity, ForceMode.VelocityChange);
                body.AddTorque(Random.insideUnitSphere * force * 0.05f, ForceMode.VelocityChange);

                Object.Destroy(piece, lifetime);
            }
        }
    }
}

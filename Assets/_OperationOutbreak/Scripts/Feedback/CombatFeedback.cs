using UnityEngine;
namespace OperationOutbreak.Feedback
{
    public static class CombatFeedback
    {
        public static void SpawnHitSpark(Vector3 position)
        {
            GameObject spark = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            spark.name = "HitSpark"; spark.transform.position = position + Vector3.up * 0.28f; spark.transform.localScale = Vector3.one * 0.34f;
            Object.Destroy(spark.GetComponent<Collider>());
            Renderer renderer = spark.GetComponent<Renderer>(); renderer.material.color = new Color(1f, .82f, .2f, 1f);
            Object.Destroy(spark, .18f);
        }
    }
}

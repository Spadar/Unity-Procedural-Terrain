using UnityEngine;
using System.Collections;

public class RemoveTerrain : MonoBehaviour {


	// Use this for initialization
	void Start () {
		ExpandTerrain.OnCoordChange += removeTerrain;
	}
	
	// Update is called once per frame
	void Update () {
	
	}

	void removeTerrain(Vector2 newCoord, ExpandTerrain source){
		int distance = source.getGridDistance (newCoord, source.getGridCoordinate (gameObject.transform.position));
		if (source.getGridDistance (newCoord, source.getGridCoordinate (gameObject.transform.position)) > source.renderDistance) {
			Debug.Log("Removing " + gameObject.name + " Distance of " + distance);
			ExpandTerrain.OnCoordChange -= removeTerrain;
			Destroy(gameObject);
		}
	}
}

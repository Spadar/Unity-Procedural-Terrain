using UnityEngine;
using AssemblyCSharp;
using System.Collections;

public class ManipulateTerrain : MonoBehaviour {

	// Use this for initialization
	void Start () {
	
	}
	
	// Update is called once per frame
	void Update () {
	
		if(Input.GetMouseButtonDown(0))
		{
			RaycastHit hit;
			
			Camera cam = (Camera)gameObject.GetComponent(typeof(Camera));
			
			if(cam != null && Physics.Raycast(cam.ScreenPointToRay(Input.mousePosition), out hit, 10000))
			{
				Vector3 hitPos = hit.point;
				
				Debug.Log(hitPos.x + "," + hitPos.y + "," + hitPos.z);
				
				Vector2 gridCoord = WorldTerrain.getGridCoordinate(hitPos);
				string terrainName = WorldTerrain.getTerrainName((int)gridCoord.x, (int)gridCoord.y);
				
				TerrainTile targetTile = null;
				
				if(WorldTerrain.terrainMap.TryGetValue(terrainName, out targetTile))
				{
					WorldTerrain.LocalCoordinate coord = WorldTerrain.WorldToLocal(hitPos, WorldTerrain.heightMapResolution);
					
					targetTile.setHeight((int)coord.localCoordinate.x, (int)coord.localCoordinate.y, targetTile.getHeight((int)coord.localCoordinate.x, (int)coord.localCoordinate.y) + 0.001f);
				}
			}
		}
		
		if(Input.GetMouseButtonDown(1))
		{
			RaycastHit hit;
			
			Camera cam = (Camera)gameObject.GetComponent(typeof(Camera));
			
			if(cam != null && Physics.Raycast(cam.ScreenPointToRay(Input.mousePosition), out hit, 10000))
			{
				Vector3 hitPos = hit.point;
				
				Debug.Log(hitPos.x + "," + hitPos.y + "," + hitPos.z);
				
				Vector2 gridCoord = WorldTerrain.getGridCoordinate(hitPos);
				string terrainName = WorldTerrain.getTerrainName((int)gridCoord.x, (int)gridCoord.y);
				
				TerrainTile targetTile = null;
				
				if(WorldTerrain.terrainMap.TryGetValue(terrainName, out targetTile))
				{
					WorldTerrain.LocalCoordinate coord = WorldTerrain.WorldToLocal(hitPos, WorldTerrain.heightMapResolution);
					
					targetTile.setHeight((int)coord.localCoordinate.x, (int)coord.localCoordinate.y, targetTile.getHeight((int)coord.localCoordinate.x, (int)coord.localCoordinate.y) - 0.001f);
				}
			}
		}
	
	}
}

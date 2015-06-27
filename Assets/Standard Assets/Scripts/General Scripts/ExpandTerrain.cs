using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using SimplexNoise;
using System.Threading;

public class ExpandTerrain : MonoBehaviour 
{
	protected class TerrainTile
	{
		public Vector2 position;
		public GameObject terrain;
		public TerrainTile(Vector2 position, GameObject terrain)
		{
			this.position = position;
			this.terrain = terrain;
		}
	}

	public GameObject terrainPrefab;
	public int renderDistance;
	
	public int heightMapResolution;
	
	public int tileSize;
	
	private Vector2 currentCoordinate;
	private float size;
	private Vector3 dataSize;
	
	private int updateCount = 0;

	public delegate void CoordChange (Vector2 newCoord, ExpandTerrain source);
	public static event CoordChange OnCoordChange;

	private Dictionary<string, TerrainTile> terrainMap = new Dictionary<string, TerrainTile>();
		
	// Use this for initialization
	void Start () {
		Terrain terrain = (Terrain)terrainPrefab.GetComponent<Terrain>();
		dataSize = new Vector3 (tileSize, tileSize, tileSize);
		size = dataSize.x;
	}
	
	// Update is called once per frame
	void Update () 
	{
		//We want to populate the surrounding X chunks around the player
		//First, we find the XY coordinate of the player
		Vector2 coordinate = getGridCoordinate (gameObject.transform.position);
		updateCount++;
		if(updateCount > 5)
		{
		updateCount = 0;
		}
		Debug.Log("Grid Coordinate: [" + coordinate.x + "," + coordinate.y + "]" );
		currentCoordinate = coordinate;
		CoordChangeEvent(currentCoordinate);
			
		cullTerrain();

		for (int x = (int)coordinate.x - renderDistance; x < (int)coordinate.x + renderDistance; x++) 
		{
			for (int y = (int)coordinate.y - renderDistance; y < (int)coordinate.y + renderDistance; y++) 
			{
				Vector2 terrainCoord = new Vector2(x,y);
				if(getGridDistance(terrainCoord,coordinate) <= renderDistance && updateCount == 0)
				{
					bool existsInMap = terrainMap.ContainsKey(getTerrainName(x,y));
					bool isNull = true;

					if(existsInMap)
					{
						isNull = terrainMap[getTerrainName(x,y)].terrain == null;
					}

					if (!existsInMap || isNull) 
					{
						//Generate the height map for the terrain in parallel
						float[,] heights = null;
						Thread heightmapThread = new Thread(delegate() 
						{
							heights = generateHeightMap(x,y);
						});
						heightmapThread.Start();

						GameObject newTile = (GameObject)Object.Instantiate (terrainPrefab);

						newTile.name = getTerrainName(x,y);

						if(terrainMap.ContainsKey(newTile.name))
						{
							terrainMap.Remove(newTile.name);
						}
						terrainMap.Add(newTile.name, new TerrainTile(new Vector2(x,y), newTile));

						newTile.transform.position = new Vector3 ((x - 250) * (size), 0, (y - 250) * (size));

						newTile.AddComponent(typeof(Terrain));
						Terrain terrain = (Terrain)newTile.GetComponent(typeof(Terrain));

						newTile.AddComponent(typeof(TerrainCollider));
						TerrainCollider collider = (TerrainCollider)newTile.GetComponent(typeof(TerrainCollider));
						
						TerrainData terrainData = new TerrainData();
						
						terrain.terrainData = terrainData;
						collider.terrainData = terrainData;

						terrainData.heightmapResolution = heightMapResolution;
						terrainData.size = dataSize;
						
						generateTerrainTexture(terrainData);
						
						//Wait in case the heightmap hasn't been generated.
						while(heights == null)
						{
						}

						terrainData.SetHeights(0,0,heights);
						updateCount++;
						break;
					}
				}
			}
		}
	}
	
	private void generateTerrainTexture(TerrainData data)
	{
		SplatPrototype[] texture = new SplatPrototype[1];
		Texture2D load = (Texture2D)Resources.Load("Grass (Hill)"); 
		texture[0] = new SplatPrototype();
		texture[0].texture = load;
		data.splatPrototypes = texture;
	}
	
	private float[,] generateHeightMap(int tileX, int tileY)
	{
		int nRows = heightMapResolution;
		int nCols = heightMapResolution;
		float[,] heights = new float[nRows, nCols];
		for(int hx = 0; hx < nRows; hx++)
		{
			for(int hy = 0; hy < nCols; hy++)
			{
				float height = (Noise.Generate((hx + ((nRows - 1) * (tileY) )) / 1000f, (hy + ((nCols - 1) * (tileX))) / 1000f))/2 + 0.5f;
				heights[hx,hy] = height;
			}
		}

		return heights;
	}

	private void cullTerrain()
	{
		foreach (TerrainTile tile in terrainMap.Values) 
		{
			if(getGridDistance(tile.position, currentCoordinate) > renderDistance)
			{
				GameObject.DestroyImmediate(tile.terrain);
				Resources.UnloadUnusedAssets();
			}
		}
	}

	private string getTerrainName(int x, int y){
		string terrainName = "Terrain:" + x + "," + y;
		return terrainName;
	}
	
	public Vector2 getGridCoordinate(Vector3 position){
		Vector2 coordinate = new Vector2 ();
		
		coordinate.x = Mathf.FloorToInt (position.x / (size)) + 250;
		coordinate.y = Mathf.FloorToInt (position.z / (size)) + 250;
		
		return coordinate;
	}
	
	public int getGridDistance(Vector2 pos1, Vector2 pos2){
		return Mathf.FloorToInt(Mathf.Sqrt ( Mathf.Pow((pos1.x - pos2.x),2) + Mathf.Pow((pos1.y - pos2.y),2)));
	}
	
	void CoordChangeEvent(Vector2 newCoord){
		if(OnCoordChange != null){
			OnCoordChange(newCoord, this);
		}
	}
}
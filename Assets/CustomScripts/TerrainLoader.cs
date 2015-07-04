using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using LibNoise;
using LibNoise.Modifiers;

public class TerrainLoader : MonoBehaviour 
{
	protected class TerrainTile
	{
		public string tileName;
		public Vector2 position;
		public int dist;
		public GameObject terrain;
		public float[,] heightMap;
		public float[,,] splatData;
		public TerrainTile(Vector2 position, string tileName, int dist, float[,] heightMap, float[,,] splatData, GameObject terrain)
		{
			this.position = position;
			this.terrain = terrain;
			this.dist = dist;
			this.heightMap = heightMap;
			this.tileName = tileName;
			this.splatData = splatData;
		}
		
		public float getHeight(int x, int y)
		{
			if(heightMap != null)
			{
				return heightMap[x,y];
			}
			else
			{
				return 0f;
			}
		}
		
		public float getSteepness(int x, int y)
		{
			float maxSteepness = 0;
			
			float centralHeight = getHeight(x,y);
			
			//Loop through all the neighboring heights
			for(int xI = -1; xI <= 1; xI++)
			{
				for(int yI = -1; yI <= 1; yI++)
				{
					int sampleX = x + xI;
					int sampleY = y + yI;
					//Don't attempt to sample if we're outside the bounds of the array.
					if(sampleX >= 0 && sampleX <= Mathf.Sqrt(heightMap.Length) - 1)
					{
						if(sampleY >= 0 && sampleY <= Mathf.Sqrt(heightMap.Length) - 1)
						{
							float steepness = Mathf.Abs(centralHeight - getHeight(sampleX, sampleY));
							if(maxSteepness < steepness)
							{
								maxSteepness = steepness;
							}
						}
					}
				}
			}
			
			return maxSteepness;
		}
	}
	
	public int renderDistance;
	
	public int heightMapResolution;
	public int alphaMapResolution;
	
	public int tileSize;
	public int ceilingHeight;
	
	public int noiseScale;
	public int seed;
	
	private Vector2 currentCoordinate;
	private float size;
	private Vector3 dataSize;
	
	private int updateCount = 0;
	
	private int alphaLayers = 4;
	
	public delegate void CoordChange (Vector2 newCoord, TerrainLoader source);
	public static event CoordChange OnCoordChange;
	
	private Dictionary<string, TerrainTile> terrainMap = new Dictionary<string, TerrainTile>();
	
	private Dictionary<string, TerrainTile> pendingTerrain = new Dictionary<string, TerrainTile>();
	
	int threadCount = 0;
	
	private float maxHeight = 0;
	private float maxSteepness = 0;
	
	IModule module;
	
	// Use this for initialization
	void Start () {
		dataSize = new Vector3 (tileSize, ceilingHeight, tileSize);
		size = dataSize.x;
		
		FastNoise fastPlanetContinents = new FastNoise(seed);
		fastPlanetContinents.Frequency = 1.5;
		
		FastBillow fastPlanetLowlands = new FastBillow();
		fastPlanetLowlands.Frequency = 4;
		LibNoise.Modifiers.ScaleBiasOutput fastPlanetLowlandsScaled = new ScaleBiasOutput(fastPlanetLowlands);
		fastPlanetLowlandsScaled.Scale = 0.2;
		fastPlanetLowlandsScaled.Bias = 0.5;
		
		FastRidgedMultifractal fastPlanetMountainsBase = new FastRidgedMultifractal(seed);
		fastPlanetMountainsBase.Frequency = 4;
		
		ScaleBiasOutput fastPlanetMountainsScaled = new ScaleBiasOutput(fastPlanetMountainsBase);
		fastPlanetMountainsScaled.Scale = 0.4;
		fastPlanetMountainsScaled.Bias = 0.85;
		
		FastTurbulence fastPlanetMountains = new FastTurbulence(fastPlanetMountainsScaled);
		fastPlanetMountains.Power = 0.1;
		fastPlanetMountains.Frequency = 50;
		
		FastNoise fastPlanetLandFilter = new FastNoise(seed + 1);
		fastPlanetLandFilter.Frequency = 6;
		
		Select fastPlanetLand = new Select(fastPlanetLandFilter, fastPlanetLowlandsScaled, fastPlanetMountains);
		fastPlanetLand.SetBounds(0, 1000);
		fastPlanetLand.EdgeFalloff = 0.5;
		
		FastBillow fastPlanetOceanBase = new FastBillow(seed);
		fastPlanetOceanBase.Frequency = 15;
		ScaleOutput fastPlanetOcean = new ScaleOutput(fastPlanetOceanBase, 0.1);
		
		Select fastPlanetFinal = new Select(fastPlanetContinents, fastPlanetOcean, fastPlanetLand);
		fastPlanetFinal.SetBounds(0, 1000);
		fastPlanetFinal.EdgeFalloff = 0.5;
		
		module = fastPlanetFinal;
	}
	
	// Update is called once per frame
	void Update () 
	{
		//We want to populate the surrounding X chunks around the player
		//First, we find the XY coordinate of the player
		Vector2 coordinate = getGridCoordinate (gameObject.transform.position);
		updateCount++;
		if(updateCount > 0)
		{
			updateCount = 0;
		}
		Debug.Log("Grid Coordinate: [" + coordinate.x + "," + coordinate.y + "]" );
		currentCoordinate = coordinate;
		CoordChangeEvent(currentCoordinate);
		
		cullTerrain();
		
		List<TerrainTile> surroundingTerrain = new List<TerrainTile>();
		
		for (int x = (int)coordinate.x - renderDistance; x < (int)coordinate.x + renderDistance; x++) 
		{
			for (int y = (int)coordinate.y - renderDistance; y < (int)coordinate.y + renderDistance; y++) 
			{
				Vector2 terrainCoord = new Vector2(x,y);
				
				string tileName = getTerrainName(x,y);
				
				bool existsInMap = terrainMap.ContainsKey(tileName);
				bool isNull = true;
				
				if(existsInMap)
				{
					isNull = terrainMap[tileName].terrain == null;
					if(isNull)
					{
						//Remove the null terrain from the terrain map
						terrainMap.Remove(tileName);
					}
				}
				
				if((!existsInMap || isNull) && getGridDistance(terrainCoord,coordinate) <= renderDistance)
				{
					surroundingTerrain.Add(new TerrainTile(terrainCoord, tileName, getGridDistance(terrainCoord, currentCoordinate), null, null, null));
				}
			}
		}
		
		//Order list by distance from player
		List<TerrainTile> orderedTerrain = surroundingTerrain.OrderBy(terrain => terrain.dist).ToList();
		
		foreach(TerrainTile tile in orderedTerrain)
		{
			if(updateCount == 0 || true)
			{
				//Generate the height map for the terrain in parallel
				if(!pendingTerrain.ContainsKey(tile.tileName) && !terrainMap.ContainsKey(tile.tileName))
				{
					tryGenerateComplexHeightMap((int)tile.position.x,(int)tile.position.y, tile);
				}
				else if(pendingTerrain.ContainsKey(tile.tileName))
				{
						TerrainTile processedTile = pendingTerrain[tile.tileName];
						if(processedTile.heightMap != null && processedTile.splatData != null)
						{							
							GameObject newTile = new GameObject();
							
							newTile.name = tile.tileName;
							
							terrainMap.Add(tile.tileName, new TerrainTile(tile.position, tile.tileName, tile.dist, processedTile.heightMap, processedTile.splatData, newTile));
							pendingTerrain.Remove(tile.tileName);
							
							newTile.transform.position = new Vector3 (((int)tile.position.x - 250) * (size), 0, ((int)tile.position.y - 250) * (size));
							
							newTile.AddComponent(typeof(Terrain));
							Terrain terrain = (Terrain)newTile.GetComponent(typeof(Terrain));
							
							newTile.AddComponent(typeof(TerrainCollider));
							TerrainCollider collider = (TerrainCollider)newTile.GetComponent(typeof(TerrainCollider));
							
							TerrainData terrainData = new TerrainData();
							
							terrain.terrainData = terrainData;
							collider.terrainData = terrainData;
							
							terrainData.heightmapResolution = heightMapResolution;
							terrainData.size = dataSize;
							
							
							terrainData.alphamapResolution = alphaMapResolution;
							
							generateTerrainTexture(terrainData);
														
							terrainData.SetHeights(0,0,processedTile.heightMap);
							
							terrainData.SetAlphamaps(0, 0, processedTile.splatData);					
							break;
						}
				}
			}
			updateCount++;
			if(updateCount > 3)
			{
				break;
			}
		}
	}
	
	private void generateTerrainTexture(TerrainData data)
	{
		SplatPrototype[] texture = new SplatPrototype[4];
		Texture2D sand = (Texture2D)Resources.Load("SandAlbedo"); 
		texture[0] = new SplatPrototype();
		texture[0].texture = sand;
		
		Texture2D grass = (Texture2D)Resources.Load("GrassHillAlbedo"); 
		texture[1] = new SplatPrototype();
		texture[1].texture = grass;
		
		Texture2D rockygrass = (Texture2D)Resources.Load("GrassRockyAlbedo"); 
		texture[2] = new SplatPrototype();
		texture[2].texture = rockygrass;
		
		Texture2D rockycliff = (Texture2D)Resources.Load("CliffAlbedoSpecular"); 
		texture[3] = new SplatPrototype();
		texture[3].texture = rockycliff;
		
		data.splatPrototypes = texture;
	}
	
	private float[,,] generateSplatMap(TerrainTile tile)
	{
		// Splatmap data is stored internally as a 3d array of floats, so declare a new empty array ready for your custom splatmap data:
		float[, ,] splatmapData = new float[alphaMapResolution, alphaMapResolution, alphaLayers];
		
		for (int y = 0; y < alphaMapResolution; y++)
		{
			for (int x = 0; x < alphaMapResolution; x++)
			{
				// Normalise x/y coordinates to range 0-1 
				float y_01 = (float)y/(float)alphaMapResolution;
				float x_01 = (float)x/(float)alphaMapResolution;
				
				int equivX = Mathf.RoundToInt(x_01 * heightMapResolution);
				int equivY = Mathf.RoundToInt(y_01 * heightMapResolution);
				
				// Sample the height at this location (note GetHeight expects int coordinates corresponding to locations in the heightmap array)
				float height = tile.getHeight(equivX,equivY);
				
				if(height > maxHeight)
				{
					maxHeight = height;
				}
				
				// Calculate the normal of the terrain (note this is in normalised coordinates relative to the overall terrain dimensions)
				//Vector3 normal = terrainData.GetInterpolatedNormal(y_01,x_01);
				
				// Calculate the steepness of the terrain
				float steepness = tile.getSteepness(equivX, equivY);
				
				if(steepness > maxSteepness)
				{
					maxSteepness = steepness;
				}
				
				// Setup an array to record the mix of texture weights at this point
				float[] splatWeights = new float[alphaLayers];
				
				// CHANGE THE RULES BELOW TO SET THE WEIGHTS OF EACH TEXTURE ON WHATEVER RULES YOU WANT
				
				float cliffThreshhold = 0.009f;
				
				//Sand
				splatWeights[0] = ((1f - height) - 0.75f) - steepness*3f;
				
				if(splatWeights[0] < 0)
				{
					splatWeights[0] = 0;
				}
				
				if(height < 0.2 && steepness < cliffThreshhold)
				{
					//splatWeights[0] = 1f;
				}
				
				//Grass
				splatWeights[1] = (height - 0.1f) - steepness*3f;
				
				if(splatWeights[1] < 0)
				{
					splatWeights[1] = 0;
				}
				
				if(height > 0.2 && height < 0.4 && steepness < cliffThreshhold)
				{
					//splatWeights[1] = 1f;
				}
				
				//RockyGrass
				splatWeights[2] = (height - 0.5f) - steepness*3f;
				
				if(splatWeights[2] < 0)
				{
					splatWeights[2] = 0;
				}
				
				if(height > 0.4 && steepness < cliffThreshhold)
				{
					//splatWeights[2] = 1f;
				}
				
				//Cliff
				splatWeights[3] = 0f;
				
				if(steepness >= cliffThreshhold)
				{
					splatWeights[3] = steepness*10f;
				}
				
				float weightSum = splatWeights.Sum();
				
				// Loop through each terrain texture
				for(int i = 0; i<alphaLayers; i++){
					// Assign this point to the splatmap array
					splatmapData[x, y, i] = splatWeights[i]/weightSum;
				}
			}
		}
		
		return splatmapData;
	}
	
	private void tryGenerateComplexHeightMap(int tileX, int tileY, TerrainTile tile)
	{
		if(threadCount < 15)
		{
			try
			{
				Interlocked.Add(ref threadCount, 1);
				Thread heightmapThread = new Thread(() =>
				                                    {
					pendingTerrain.Add(tile.tileName, new TerrainTile(tile.position, tile.tileName, tile.dist, null, null, null));
					int nRows = heightMapResolution;
					int nCols = heightMapResolution;
					float[,] heights = new float[nRows, nCols];
					
					for (int y = 0; y < nCols; y++)
					{
						for (int x = 0; x < nRows; x++)
						{
							heights[x,y] = (float)((module.GetValue(((double)x + (((double)nRows - 1) * ((double)tileY))) / (double)noiseScale,((double)y + (((double)nCols - 1) * ((double)tileX))) / (double)noiseScale, 10)/1.5) + 0.07);
						}
					}
					
					TerrainTile partialTile = new TerrainTile(tile.position, tile.tileName, tile.dist, heights, null, null);
					
					partialTile.splatData = generateSplatMap(partialTile);
					
					pendingTerrain[tile.tileName] = partialTile;
					
					Interlocked.Add(ref threadCount, -1);
				});
				
				heightmapThread.Start();
			}
			catch(UnityException)
			{
				Interlocked.Add(ref threadCount, -1);
			}
		}
	}
	
	private void cullTerrain()
	{
		List<TerrainTile> removed = new List<TerrainTile>();
		foreach (TerrainTile tile in terrainMap.Values) 
		{
			if(getGridDistance(tile.position, currentCoordinate) > renderDistance)
			{
				GameObject.DestroyImmediate(tile.terrain);
				Resources.UnloadUnusedAssets();
				removed.Add(tile);
			}
		}
		
		foreach(TerrainTile tile in removed)
		{
			terrainMap.Remove(getTerrainName((int)tile.position.x, (int)tile.position.y));
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
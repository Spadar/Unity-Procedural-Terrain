using UnityEngine;
using System.Collections;

public class PreventFallingUnderMap : MonoBehaviour
{
	int scanCounter = 0;

	// Use this for initialization
	void Start ()
	{
	
	}
	
	// Update is called once per frame
	void Update ()
	{
		if(scanCounter == 0)
		{
			RaycastHit rayHit = new RaycastHit();
			Physics.Raycast(Camera.main.transform.position, new Vector3(0,1,0), out rayHit);
			
			if(rayHit.distance > 0)
			{
				if(rayHit.transform.gameObject.GetComponents<Terrain>().Length > 0)
				{
					Vector3 curPos = gameObject.transform.position;
					
					gameObject.transform.position = new Vector3(curPos.x, rayHit.point.y + 1, curPos.z);
				}
			}
		}
		
		scanCounter++;
		if(scanCounter > 50)
		{
			scanCounter = 0;
		}
	}
}


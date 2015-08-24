using UnityEngine;
using System.Collections;

public class DayNightCycle : MonoBehaviour {
	
	public int dayLength;
	
	// Use this for initialization
	void Start () {
	
	}
	
	// Update is called once per frame
	void Update () {
		float rotStep = 360f/dayLength;
		
		
		gameObject.transform.Rotate(new Vector3(rotStep*Time.deltaTime,rotStep*Time.deltaTime,0));
	}
}

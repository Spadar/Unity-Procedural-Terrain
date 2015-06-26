using UnityEngine;
using System.Collections;

public class PushRigidBody : MonoBehaviour {

	// Use this for initialization
	void Start () {
	
	}
	
	// Update is called once per frame
	void Update () {
			Camera playerCam = Camera.main;
			Ray ray = playerCam.ScreenPointToRay(new Vector3(Screen.width/2, Screen.height/2, 0));
			RaycastHit hit;
			if(Physics.Raycast(ray, out hit, 100)){

				if(hit.collider.attachedRigidbody != null){
					Debug.Log("Found something to push...");
					Rigidbody body = hit.collider.attachedRigidbody;
					body.AddForceAtPosition(new Vector3(0,1,0) * 1000, hit.point);
				}
			}
	}
}

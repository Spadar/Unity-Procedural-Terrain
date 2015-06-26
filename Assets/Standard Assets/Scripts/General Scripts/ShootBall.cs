using UnityEngine;
using System.Collections;

public class ShootBall : MonoBehaviour {

	public GameObject prefab;

	void Start() {
	}

	void Update() {
		if(Input.GetButtonUp("Fire1")){
			Ray ray = Camera.main.ScreenPointToRay(new Vector3(Screen.width/2, Screen.height/2, 0));
			GameObject test = (GameObject)Instantiate(prefab, gameObject.transform.position + (ray.direction * 2), new Quaternion(0,0,0,0));
			test.rigidbody.velocity = ray.direction * 50;

		}
	}
}

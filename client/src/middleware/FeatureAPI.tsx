/* eslint-disable no-trailing-spaces */
/* eslint-disable indent */
/* eslint-disable max-len */

import React from 'react';

class FeatureAPI {

  static Add (featureData: any): Promise<any>
  {
    console.log("Add ()");
    
    // var jsonData = {      
      // "cave": [
          // {
    let defaultRequestData = {
      // "user_id": 1,
      // "type_id": featureData.type_id ? featureData.type_id : 1,

      // "updated_at": "2022-06-25",
      // "created_at": "2022-06-25",

      // "area": 1
    };

    // var objectC = {...objectA, ...objectB};
    // const obj3 = Object.assign({}, obj1, obj2);
    let formData = {...featureData, ...defaultRequestData};

    console.log("formData=");
    console.log(formData);
    /*var formData = {
              "name": "cave2",
              "rock_type": "unknown",
              "type_id": 1,
              "user_id": 1,
               "updated_at": "2022-06-25",
               "created_at": "2022-06-25",
          // }
      // ]
    };*/

    /*let formBodyArr = [];
for (var property in formData) {
  var encodedKey = encodeURIComponent(property);
  var encodedValue = encodeURIComponent(formData[property]);
  formBodyArr.push(encodedKey + "=" + encodedValue);
}
    let formBody = formBodyArr.join("&");
    // console.log("formBody");
    // console.log(formBody);
*/
    
      return fetch("http://localhost/silexgis/server/public/api/features", {
        method: 'POST', 
        mode: 'cors', 
        cache: 'no-cache', // *default, no-cache, reload, force-cache, only-if-cached
        // credentials: 'same-origin', // include, *same-origin, omit
        headers: {
          // 'Content-Type': 'application/x-www-form-urlencoded;charset=UTF-8'
          'Content-Type': 'application/json'
        },
        redirect: 'follow', // manual, *follow, error
        // body: new URLSearchParams(formData) // formBody //formData
        body: JSON.stringify(formData)
        // body: JSON.stringify(jsonData) // body data type must match "Content-Type" header
      });

      // .then(res => res.json())
      // .then(
      //   (result) => {
      //     console.log("fetch then");

      //     // this.setState({
      //     //   isLoaded: true,
      //     //   items: result.data
      //     // });
      //   },
      //   // Note: it's important to handle errors here
      //   // instead of a catch() block so that we don't swallow
      //   // exceptions from actual bugs in components.
      //   (error) => {
      //     // this.setState({
      //     //   isLoaded: true,
      //     //   error
      //     // });
      //   }
      // )

      // return fetch("http://localhost/silexgis/server/public/api/features", {
      //   method: 'POST',
      //   mode: 'cors', 
      //   headers: {
      //     'Content-Type': 'application/x-www-form-urlencoded;charset=UTF-8'
      //   },      
      //   body: new URLSearchParams(formData) // formBody //formData
      //   // body: JSON.stringify(jsonData) // body data type must match "Content-Type" header
      // });  
  }

  static Save (featureData: any): Promise<any>
  {
    console.log("Save ()");
    console.log(featureData);
    
    // var jsonData = {      
      // "cave": [
          // {
    let defaultRequestData = {
      "user_id": 1,
      "type_id": featureData.type_id ? featureData.type_id : 1,

      "updated_at": "2022-06-25",
      // "created_at": "2022-06-25",

    };

    let formData = {...featureData, ...defaultRequestData};

    console.log("formData=");
    console.log(formData);
    
    return fetch("http://localhost/silexgis/server/public/api/features/" + featureData.id, {
    //return fetch("http://localhost/silexgis/server/public/api/features/update/" + featureData.id, {
      method: 'PUT', 
      mode: 'cors', 
      headers: {
        // 'Content-Type': 'application/x-www-form-urlencoded;charset=UTF-8'
        'Content-Type': 'application/json'
      },
      // body: new URLSearchParams(formData) // formBody //formData
      body: JSON.stringify(formData)
      // body: JSON.stringify(jsonData) // body data type must match "Content-Type" header
    });
  }

  static Delete (caveId: any): Promise<any>
  {
    console.log(`Delete (${caveId})`);
        
    return fetch("http://localhost/silexgis/server/public/api/features/" + caveId, {    
      method: 'DELETE', 
      mode: 'cors', 
      headers: {
        // 'Content-Type': 'application/x-www-form-urlencoded;charset=UTF-8'
        'Content-Type': 'application/json'
      },
      // body: new URLSearchParams(formData) // formBody //formData
    });
  }

  static GetAll (): Promise<any>
  {
    console.log("GetAll ()");

    const fetchData = async () : Promise<any> => {
      const result = await fetch('http://localhost/silexgis/server/public/api/features');
        
      console.log (result);
      // setProducts (result);
      return result;
    };

    return fetchData();
    
    //
  }

  static GetById (id): Promise<any>
  {
    console.log("GetById ()");

    const fetchData = async () : Promise<any> => {
      const result = await fetch('http://localhost/silexgis/server/public/api/features/' + id);
        
      console.log (result);
      // setProducts (result);
      return result;
    };

    return fetchData();
    
    //
  }

}

export default FeatureAPI;
import React from 'react';
import { Collapse } from 'antd';

import './FeatureToolbar.less';
// import FeatureTypeAPI from '../middleware/FeatureTypeAPI';

// import {EventEmitter} from "events";
import postal from 'postal';

export default class FeatureToolbar extends React.Component {
  constructor (props: any) {
    super(props);
    this.state = {
      error: null,
      // isLoaded: false,
      // featureTypeList: [], moved in HOC
      enabledFeature: undefined
    };

    this.handleFeatureClick = this.handleFeatureClick.bind(this);
    this.onCollapseChange = this.onCollapseChange.bind(this);
  }

  handleFeatureClick (event, feature) {
    console.log("handleFeatureClick ()");
    console.log(feature);

    this.setState({ enabledFeature: feature });
    // this.props.onFeatureSelection(feature);

    postal.publish({
      channel: "mainmap",
      topic: "feature.draw",
      data: {
        feature: feature
      }
    });
  }

  onCollapseChange (event) {

  }

  componentDidMount () {
    console.log("componentDidMount ()");

    // feature type data loaded in HOC (higher order component)
    // this.loadData();
  }

/*  private loadData() {
    // from https://stackoverflow.com/a/18837750
    function imageExists(image_url){

      var http = new XMLHttpRequest();
  
      http.open('HEAD', image_url, false);
      http.send();
  
      return http.status != 404;  
    }

    FeatureTypeAPI.GetAll().then(res => res.json())
      .then(
        (res) => {
          console.log('get response data ok');

          console.log(res);

          for (let index = 0; index < res.data.length; index++)
          {
            res.data[index].key = res.data[index].id;

            let symbolImageNotFound = false;

            if ((res.data[index].symbol_path == null) // this checks both null and undefined
                || (symbolImageNotFound = !imageExists('feature_symbols/' + res.data[index].symbol_path)))
            {
              if (symbolImageNotFound)
                console.warn(`symbol resource not found for for feature id=${res.data[index].id} title='${res.data[index].name} symbol_path='${res.data[index].symbol_path}''`);

              res.data[index].symbol_path = 'generic_feature.png';
            }
          }

          this.setState({ 
            isLoaded: true,
            featureTypeList: res.data 
          });

          ;
          // Promise.resolve(result);
        },
        (error) => {
        }
      );
  }
*/
  render () {
    const { Panel } = Collapse;

    const { error, /*isLoaded, featureTypeList,*/ enabledFeature } = this.state;

    const featureTypeList = this.props.featureTypes;

    let featureEnabledStyle = {
      border: '1px solid black',
      fontWeight: 'bold',
      // 'font-weight': 'bold',
      background: '#F5F5F5'
    };

    // console.log("");
    // console.log("render featuretoolbar");
    // console.log(featureTypeList);

    if (featureTypeList == null)
      featureTypeList = {};

    let groupedFeatureTypes = {};

    // for (let [key, value] of Object.entries(meals)) {
    //   console.log(key + ':' + value);
    // }
    // for (var prop in obj) {
    //     if (Object.prototype.hasOwnProperty.call(obj, prop)) {
    //         // do stuff
    //     }
    // }
    // Object.keys(obj).forEach(function(key,index) {
    //     // key: the name of the object key
    //     // index: the ordinal position of the key within the object 
    // });
    // for (const [key, value] of Object.entries(obj)) { }
    // Object.entries(obj).forEach(([key, value]) => ...)
    // for (const value of Object.values(obj)) { }
    // Object.values(obj).forEach(value => ...)
    

    // Object.values(featureTypeList).forEach(value => ...)

    for (const [key, ft] of Object.entries(featureTypeList))
    {
      if (groupedFeatureTypes[ft.group_identifier])
        groupedFeatureTypes[ft.group_identifier].items.push(ft);
      else
        groupedFeatureTypes[ft.group_identifier] = {
          title: ft.group_title,
          identifier: ft.group_identifier,
          items: [ft]
        }
    }

    console.log("groupedFeatureTypes");
    console.log(groupedFeatureTypes);

    /*if (error) {
      return <div>Error: {error.message}</div>;
    }
    else if (!isLoaded) {
      return <div>Loading features...</div>;
    } else {
      */
      return (
        // <ul>
          
          <Collapse defaultActiveKey={'surface_feature'} onChange={this.onCollapseChange} >
          { Object.values(groupedFeatureTypes).map(gft => ( //note: Object.values() might not be supported on some browsers
            <Panel header={gft.title} key={gft.identifier}>
              {/* <p>{text}</p> */}
              {
                gft.items.map(ft => ( //note: Object.values() might not be supported on some browsers
                <span 
                  key={ft.id} 
                  onClick={event => this.handleFeatureClick(event, ft)} 
                  style={ft == enabledFeature ? featureEnabledStyle : {}}
                >
                  <img 
                    src={"./feature_symbols/" + (ft.symbol_path ? ft.symbol_path : "generic_feature.png")} 
                    height={24} width={24}
                  />
                  <a key={ft.id}>
                    {ft.name}
                  </a><br/>
                </span>
              ))}
            </Panel>
          ))}
          </Collapse>
        // </ul>
      );
    // }
  }
}
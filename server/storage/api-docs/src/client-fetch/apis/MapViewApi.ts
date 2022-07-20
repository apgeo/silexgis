/* tslint:disable */
/* eslint-disable */
/**
 * 
 * 
 *
 * 
 * 
 *
 * NOTE: This class is auto generated by OpenAPI Generator (https://openapi-generator.tech).
 * https://openapi-generator.tech
 * Do not edit the class manually.
 */


import * as runtime from '../runtime';

export interface 01375ebe2deaa8778d005ccf378ba312Request {
    name?: string;
}

export interface 7781581fa54d42b16f38aa39e5ca0553Request {
    id: number;
}

export interface 8fe83ba341aa4553dcc2c0d02158caffRequest {
    id: number;
    name?: string;
}

export interface D8860be06ffbb834f065f28b4ca24063Request {
    id: number;
}

/**
 * 
 */
export class MapViewApi extends runtime.BaseAPI {

    /**
     * Create MapView
     * createMapView
     */
    async _01375ebe2deaa8778d005ccf378ba312Raw(requestParameters: 01375ebe2deaa8778d005ccf378ba312Request, initOverrides?: RequestInit | runtime.InitOverrideFunction): Promise<runtime.ApiResponse<void>> {
        const queryParameters: any = {};

        const headerParameters: runtime.HTTPHeaders = {};

        const consumes: runtime.Consume[] = [
            { contentType: 'application/x-www-form-urlencoded' },
        ];
        // @ts-ignore: canConsumeForm may be unused
        const canConsumeForm = runtime.canConsumeForm(consumes);

        let formParams: { append(param: string, value: any): any };
        let useForm = false;
        if (useForm) {
            formParams = new FormData();
        } else {
            formParams = new URLSearchParams();
        }

        if (requestParameters.name !== undefined) {
            formParams.append('name', requestParameters.name as any);
        }

        const response = await this.request({
            path: `/mapViews`,
            method: 'POST',
            headers: headerParameters,
            query: queryParameters,
            body: formParams,
        }, initOverrides);

        return new runtime.VoidApiResponse(response);
    }

    /**
     * Create MapView
     * createMapView
     */
    async _01375ebe2deaa8778d005ccf378ba312(requestParameters: 01375ebe2deaa8778d005ccf378ba312Request = {}, initOverrides?: RequestInit | runtime.InitOverrideFunction): Promise<void> {
        await this._01375ebe2deaa8778d005ccf378ba312Raw(requestParameters, initOverrides);
    }

    /**
     * Delete MapView
     * deleteMapView
     */
    async _7781581fa54d42b16f38aa39e5ca0553Raw(requestParameters: 7781581fa54d42b16f38aa39e5ca0553Request, initOverrides?: RequestInit | runtime.InitOverrideFunction): Promise<runtime.ApiResponse<void>> {
        if (requestParameters.id === null || requestParameters.id === undefined) {
            throw new runtime.RequiredError('id','Required parameter requestParameters.id was null or undefined when calling _7781581fa54d42b16f38aa39e5ca0553.');
        }

        const queryParameters: any = {};

        const headerParameters: runtime.HTTPHeaders = {};

        const response = await this.request({
            path: `/mapViews/{id}`.replace(`{${"id"}}`, encodeURIComponent(String(requestParameters.id))),
            method: 'DELETE',
            headers: headerParameters,
            query: queryParameters,
        }, initOverrides);

        return new runtime.VoidApiResponse(response);
    }

    /**
     * Delete MapView
     * deleteMapView
     */
    async _7781581fa54d42b16f38aa39e5ca0553(requestParameters: 7781581fa54d42b16f38aa39e5ca0553Request, initOverrides?: RequestInit | runtime.InitOverrideFunction): Promise<void> {
        await this._7781581fa54d42b16f38aa39e5ca0553Raw(requestParameters, initOverrides);
    }

    /**
     * Get all MapViews
     * getMapViewList
     */
    async _8310101e74c64a50acdf3ab482d91216Raw(initOverrides?: RequestInit | runtime.InitOverrideFunction): Promise<runtime.ApiResponse<void>> {
        const queryParameters: any = {};

        const headerParameters: runtime.HTTPHeaders = {};

        const response = await this.request({
            path: `/mapViews`,
            method: 'GET',
            headers: headerParameters,
            query: queryParameters,
        }, initOverrides);

        return new runtime.VoidApiResponse(response);
    }

    /**
     * Get all MapViews
     * getMapViewList
     */
    async _8310101e74c64a50acdf3ab482d91216(initOverrides?: RequestInit | runtime.InitOverrideFunction): Promise<void> {
        await this._8310101e74c64a50acdf3ab482d91216Raw(initOverrides);
    }

    /**
     * Update MapView
     * updateMapView
     */
    async _8fe83ba341aa4553dcc2c0d02158caffRaw(requestParameters: 8fe83ba341aa4553dcc2c0d02158caffRequest, initOverrides?: RequestInit | runtime.InitOverrideFunction): Promise<runtime.ApiResponse<void>> {
        if (requestParameters.id === null || requestParameters.id === undefined) {
            throw new runtime.RequiredError('id','Required parameter requestParameters.id was null or undefined when calling _8fe83ba341aa4553dcc2c0d02158caff.');
        }

        const queryParameters: any = {};

        const headerParameters: runtime.HTTPHeaders = {};

        const consumes: runtime.Consume[] = [
            { contentType: 'application/x-www-form-urlencoded' },
        ];
        // @ts-ignore: canConsumeForm may be unused
        const canConsumeForm = runtime.canConsumeForm(consumes);

        let formParams: { append(param: string, value: any): any };
        let useForm = false;
        if (useForm) {
            formParams = new FormData();
        } else {
            formParams = new URLSearchParams();
        }

        if (requestParameters.name !== undefined) {
            formParams.append('name', requestParameters.name as any);
        }

        const response = await this.request({
            path: `/mapViews/{id}`.replace(`{${"id"}}`, encodeURIComponent(String(requestParameters.id))),
            method: 'PUT',
            headers: headerParameters,
            query: queryParameters,
            body: formParams,
        }, initOverrides);

        return new runtime.VoidApiResponse(response);
    }

    /**
     * Update MapView
     * updateMapView
     */
    async _8fe83ba341aa4553dcc2c0d02158caff(requestParameters: 8fe83ba341aa4553dcc2c0d02158caffRequest, initOverrides?: RequestInit | runtime.InitOverrideFunction): Promise<void> {
        await this._8fe83ba341aa4553dcc2c0d02158caffRaw(requestParameters, initOverrides);
    }

    /**
     * Get MapView
     * getMapViewItem
     */
    async d8860be06ffbb834f065f28b4ca24063Raw(requestParameters: D8860be06ffbb834f065f28b4ca24063Request, initOverrides?: RequestInit | runtime.InitOverrideFunction): Promise<runtime.ApiResponse<void>> {
        if (requestParameters.id === null || requestParameters.id === undefined) {
            throw new runtime.RequiredError('id','Required parameter requestParameters.id was null or undefined when calling d8860be06ffbb834f065f28b4ca24063.');
        }

        const queryParameters: any = {};

        const headerParameters: runtime.HTTPHeaders = {};

        const response = await this.request({
            path: `/mapViews/{id}`.replace(`{${"id"}}`, encodeURIComponent(String(requestParameters.id))),
            method: 'GET',
            headers: headerParameters,
            query: queryParameters,
        }, initOverrides);

        return new runtime.VoidApiResponse(response);
    }

    /**
     * Get MapView
     * getMapViewItem
     */
    async d8860be06ffbb834f065f28b4ca24063(requestParameters: D8860be06ffbb834f065f28b4ca24063Request, initOverrides?: RequestInit | runtime.InitOverrideFunction): Promise<void> {
        await this.d8860be06ffbb834f065f28b4ca24063Raw(requestParameters, initOverrides);
    }

}
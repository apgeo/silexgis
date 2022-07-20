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

export interface 2da30fec0600f1315a69e6b7df036264Request {
    id: number;
}

export interface 5aa6d43be787f05232b50135abc8d28aRequest {
    id: number;
}

export interface 5b7c00ad16af2bbc526833a99c0c7549Request {
    name?: string;
}

export interface Eb681b0bf23941209b4da2efc88dfb89Request {
    id: number;
    name?: string;
}

/**
 * 
 */
export class GeoreferencedMapApi extends runtime.BaseAPI {

    /**
     * Delete GeoreferencedMap
     * deleteGeoreferencedMap
     */
    async _2da30fec0600f1315a69e6b7df036264Raw(requestParameters: 2da30fec0600f1315a69e6b7df036264Request, initOverrides?: RequestInit | runtime.InitOverrideFunction): Promise<runtime.ApiResponse<void>> {
        if (requestParameters.id === null || requestParameters.id === undefined) {
            throw new runtime.RequiredError('id','Required parameter requestParameters.id was null or undefined when calling _2da30fec0600f1315a69e6b7df036264.');
        }

        const queryParameters: any = {};

        const headerParameters: runtime.HTTPHeaders = {};

        const response = await this.request({
            path: `/georeferencedMaps/{id}`.replace(`{${"id"}}`, encodeURIComponent(String(requestParameters.id))),
            method: 'DELETE',
            headers: headerParameters,
            query: queryParameters,
        }, initOverrides);

        return new runtime.VoidApiResponse(response);
    }

    /**
     * Delete GeoreferencedMap
     * deleteGeoreferencedMap
     */
    async _2da30fec0600f1315a69e6b7df036264(requestParameters: 2da30fec0600f1315a69e6b7df036264Request, initOverrides?: RequestInit | runtime.InitOverrideFunction): Promise<void> {
        await this._2da30fec0600f1315a69e6b7df036264Raw(requestParameters, initOverrides);
    }

    /**
     * Get GeoreferencedMap
     * getGeoreferencedMapItem
     */
    async _5aa6d43be787f05232b50135abc8d28aRaw(requestParameters: 5aa6d43be787f05232b50135abc8d28aRequest, initOverrides?: RequestInit | runtime.InitOverrideFunction): Promise<runtime.ApiResponse<void>> {
        if (requestParameters.id === null || requestParameters.id === undefined) {
            throw new runtime.RequiredError('id','Required parameter requestParameters.id was null or undefined when calling _5aa6d43be787f05232b50135abc8d28a.');
        }

        const queryParameters: any = {};

        const headerParameters: runtime.HTTPHeaders = {};

        const response = await this.request({
            path: `/georeferencedMaps/{id}`.replace(`{${"id"}}`, encodeURIComponent(String(requestParameters.id))),
            method: 'GET',
            headers: headerParameters,
            query: queryParameters,
        }, initOverrides);

        return new runtime.VoidApiResponse(response);
    }

    /**
     * Get GeoreferencedMap
     * getGeoreferencedMapItem
     */
    async _5aa6d43be787f05232b50135abc8d28a(requestParameters: 5aa6d43be787f05232b50135abc8d28aRequest, initOverrides?: RequestInit | runtime.InitOverrideFunction): Promise<void> {
        await this._5aa6d43be787f05232b50135abc8d28aRaw(requestParameters, initOverrides);
    }

    /**
     * Create GeoreferencedMap
     * createGeoreferencedMap
     */
    async _5b7c00ad16af2bbc526833a99c0c7549Raw(requestParameters: 5b7c00ad16af2bbc526833a99c0c7549Request, initOverrides?: RequestInit | runtime.InitOverrideFunction): Promise<runtime.ApiResponse<void>> {
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
            path: `/georeferencedMaps`,
            method: 'POST',
            headers: headerParameters,
            query: queryParameters,
            body: formParams,
        }, initOverrides);

        return new runtime.VoidApiResponse(response);
    }

    /**
     * Create GeoreferencedMap
     * createGeoreferencedMap
     */
    async _5b7c00ad16af2bbc526833a99c0c7549(requestParameters: 5b7c00ad16af2bbc526833a99c0c7549Request = {}, initOverrides?: RequestInit | runtime.InitOverrideFunction): Promise<void> {
        await this._5b7c00ad16af2bbc526833a99c0c7549Raw(requestParameters, initOverrides);
    }

    /**
     * Get all GeoreferencedMaps
     * getGeoreferencedMapList
     */
    async dedf39617ecb7af96d8e8d48810b5729Raw(initOverrides?: RequestInit | runtime.InitOverrideFunction): Promise<runtime.ApiResponse<void>> {
        const queryParameters: any = {};

        const headerParameters: runtime.HTTPHeaders = {};

        const response = await this.request({
            path: `/georeferencedMaps`,
            method: 'GET',
            headers: headerParameters,
            query: queryParameters,
        }, initOverrides);

        return new runtime.VoidApiResponse(response);
    }

    /**
     * Get all GeoreferencedMaps
     * getGeoreferencedMapList
     */
    async dedf39617ecb7af96d8e8d48810b5729(initOverrides?: RequestInit | runtime.InitOverrideFunction): Promise<void> {
        await this.dedf39617ecb7af96d8e8d48810b5729Raw(initOverrides);
    }

    /**
     * Update GeoreferencedMap
     * updateGeoreferencedMap
     */
    async eb681b0bf23941209b4da2efc88dfb89Raw(requestParameters: Eb681b0bf23941209b4da2efc88dfb89Request, initOverrides?: RequestInit | runtime.InitOverrideFunction): Promise<runtime.ApiResponse<void>> {
        if (requestParameters.id === null || requestParameters.id === undefined) {
            throw new runtime.RequiredError('id','Required parameter requestParameters.id was null or undefined when calling eb681b0bf23941209b4da2efc88dfb89.');
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
            path: `/georeferencedMaps/{id}`.replace(`{${"id"}}`, encodeURIComponent(String(requestParameters.id))),
            method: 'PUT',
            headers: headerParameters,
            query: queryParameters,
            body: formParams,
        }, initOverrides);

        return new runtime.VoidApiResponse(response);
    }

    /**
     * Update GeoreferencedMap
     * updateGeoreferencedMap
     */
    async eb681b0bf23941209b4da2efc88dfb89(requestParameters: Eb681b0bf23941209b4da2efc88dfb89Request, initOverrides?: RequestInit | runtime.InitOverrideFunction): Promise<void> {
        await this.eb681b0bf23941209b4da2efc88dfb89Raw(requestParameters, initOverrides);
    }

}

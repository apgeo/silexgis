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

export interface 0046884845bd07f1b308a84ee5c78a43Request {
    name?: string;
}

export interface 9ffd305291fd52a23ff9cd5990ef3848Request {
    id: number;
}

export interface Bb5a9e8f110f4c16c1f1f0ad114a8469Request {
    id: number;
    name?: string;
}

export interface D55d325b3f4a902ddddb46d59b3f43f3Request {
    id: number;
}

/**
 * 
 */
export class FeatureApi extends runtime.BaseAPI {

    /**
     * Create Feature
     * createFeature
     */
    async _0046884845bd07f1b308a84ee5c78a43Raw(requestParameters: 0046884845bd07f1b308a84ee5c78a43Request, initOverrides?: RequestInit | runtime.InitOverrideFunction): Promise<runtime.ApiResponse<void>> {
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
            path: `/features`,
            method: 'POST',
            headers: headerParameters,
            query: queryParameters,
            body: formParams,
        }, initOverrides);

        return new runtime.VoidApiResponse(response);
    }

    /**
     * Create Feature
     * createFeature
     */
    async _0046884845bd07f1b308a84ee5c78a43(requestParameters: 0046884845bd07f1b308a84ee5c78a43Request = {}, initOverrides?: RequestInit | runtime.InitOverrideFunction): Promise<void> {
        await this._0046884845bd07f1b308a84ee5c78a43Raw(requestParameters, initOverrides);
    }

    /**
     * Get all Features
     * getFeatureList
     */
    async _9521496ff8ea68c45217ad52ca6dd01dRaw(initOverrides?: RequestInit | runtime.InitOverrideFunction): Promise<runtime.ApiResponse<void>> {
        const queryParameters: any = {};

        const headerParameters: runtime.HTTPHeaders = {};

        const response = await this.request({
            path: `/features`,
            method: 'GET',
            headers: headerParameters,
            query: queryParameters,
        }, initOverrides);

        return new runtime.VoidApiResponse(response);
    }

    /**
     * Get all Features
     * getFeatureList
     */
    async _9521496ff8ea68c45217ad52ca6dd01d(initOverrides?: RequestInit | runtime.InitOverrideFunction): Promise<void> {
        await this._9521496ff8ea68c45217ad52ca6dd01dRaw(initOverrides);
    }

    /**
     * Delete Feature
     * deleteFeature
     */
    async _9ffd305291fd52a23ff9cd5990ef3848Raw(requestParameters: 9ffd305291fd52a23ff9cd5990ef3848Request, initOverrides?: RequestInit | runtime.InitOverrideFunction): Promise<runtime.ApiResponse<void>> {
        if (requestParameters.id === null || requestParameters.id === undefined) {
            throw new runtime.RequiredError('id','Required parameter requestParameters.id was null or undefined when calling _9ffd305291fd52a23ff9cd5990ef3848.');
        }

        const queryParameters: any = {};

        const headerParameters: runtime.HTTPHeaders = {};

        const response = await this.request({
            path: `/features/{id}`.replace(`{${"id"}}`, encodeURIComponent(String(requestParameters.id))),
            method: 'DELETE',
            headers: headerParameters,
            query: queryParameters,
        }, initOverrides);

        return new runtime.VoidApiResponse(response);
    }

    /**
     * Delete Feature
     * deleteFeature
     */
    async _9ffd305291fd52a23ff9cd5990ef3848(requestParameters: 9ffd305291fd52a23ff9cd5990ef3848Request, initOverrides?: RequestInit | runtime.InitOverrideFunction): Promise<void> {
        await this._9ffd305291fd52a23ff9cd5990ef3848Raw(requestParameters, initOverrides);
    }

    /**
     * Update Feature
     * updateFeature
     */
    async bb5a9e8f110f4c16c1f1f0ad114a8469Raw(requestParameters: Bb5a9e8f110f4c16c1f1f0ad114a8469Request, initOverrides?: RequestInit | runtime.InitOverrideFunction): Promise<runtime.ApiResponse<void>> {
        if (requestParameters.id === null || requestParameters.id === undefined) {
            throw new runtime.RequiredError('id','Required parameter requestParameters.id was null or undefined when calling bb5a9e8f110f4c16c1f1f0ad114a8469.');
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
            path: `/features/{id}`.replace(`{${"id"}}`, encodeURIComponent(String(requestParameters.id))),
            method: 'PUT',
            headers: headerParameters,
            query: queryParameters,
            body: formParams,
        }, initOverrides);

        return new runtime.VoidApiResponse(response);
    }

    /**
     * Update Feature
     * updateFeature
     */
    async bb5a9e8f110f4c16c1f1f0ad114a8469(requestParameters: Bb5a9e8f110f4c16c1f1f0ad114a8469Request, initOverrides?: RequestInit | runtime.InitOverrideFunction): Promise<void> {
        await this.bb5a9e8f110f4c16c1f1f0ad114a8469Raw(requestParameters, initOverrides);
    }

    /**
     * Get Feature
     * getFeatureItem
     */
    async d55d325b3f4a902ddddb46d59b3f43f3Raw(requestParameters: D55d325b3f4a902ddddb46d59b3f43f3Request, initOverrides?: RequestInit | runtime.InitOverrideFunction): Promise<runtime.ApiResponse<void>> {
        if (requestParameters.id === null || requestParameters.id === undefined) {
            throw new runtime.RequiredError('id','Required parameter requestParameters.id was null or undefined when calling d55d325b3f4a902ddddb46d59b3f43f3.');
        }

        const queryParameters: any = {};

        const headerParameters: runtime.HTTPHeaders = {};

        const response = await this.request({
            path: `/features/{id}`.replace(`{${"id"}}`, encodeURIComponent(String(requestParameters.id))),
            method: 'GET',
            headers: headerParameters,
            query: queryParameters,
        }, initOverrides);

        return new runtime.VoidApiResponse(response);
    }

    /**
     * Get Feature
     * getFeatureItem
     */
    async d55d325b3f4a902ddddb46d59b3f43f3(requestParameters: D55d325b3f4a902ddddb46d59b3f43f3Request, initOverrides?: RequestInit | runtime.InitOverrideFunction): Promise<void> {
        await this.d55d325b3f4a902ddddb46d59b3f43f3Raw(requestParameters, initOverrides);
    }

}
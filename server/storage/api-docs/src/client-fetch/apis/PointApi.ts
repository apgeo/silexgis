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

export interface 5a5298550ce46a9468da1fc44a5851e2Request {
    id: number;
    name?: string;
}

export interface 7b9b6b981e45df08007c3f1a42a57595Request {
    name?: string;
}

export interface 8382eb01398d1029539a633a18910721Request {
    id: number;
}

export interface 84890aa276d26faec598361de21ed829Request {
    id: number;
}

/**
 * 
 */
export class PointApi extends runtime.BaseAPI {

    /**
     * Get all Points
     * getPointList
     */
    async _414e326e5df569e950277de2c8e9c3b1Raw(initOverrides?: RequestInit | runtime.InitOverrideFunction): Promise<runtime.ApiResponse<void>> {
        const queryParameters: any = {};

        const headerParameters: runtime.HTTPHeaders = {};

        const response = await this.request({
            path: `/points`,
            method: 'GET',
            headers: headerParameters,
            query: queryParameters,
        }, initOverrides);

        return new runtime.VoidApiResponse(response);
    }

    /**
     * Get all Points
     * getPointList
     */
    async _414e326e5df569e950277de2c8e9c3b1(initOverrides?: RequestInit | runtime.InitOverrideFunction): Promise<void> {
        await this._414e326e5df569e950277de2c8e9c3b1Raw(initOverrides);
    }

    /**
     * Update Point
     * updatePoint
     */
    async _5a5298550ce46a9468da1fc44a5851e2Raw(requestParameters: 5a5298550ce46a9468da1fc44a5851e2Request, initOverrides?: RequestInit | runtime.InitOverrideFunction): Promise<runtime.ApiResponse<void>> {
        if (requestParameters.id === null || requestParameters.id === undefined) {
            throw new runtime.RequiredError('id','Required parameter requestParameters.id was null or undefined when calling _5a5298550ce46a9468da1fc44a5851e2.');
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
            path: `/points/{id}`.replace(`{${"id"}}`, encodeURIComponent(String(requestParameters.id))),
            method: 'PUT',
            headers: headerParameters,
            query: queryParameters,
            body: formParams,
        }, initOverrides);

        return new runtime.VoidApiResponse(response);
    }

    /**
     * Update Point
     * updatePoint
     */
    async _5a5298550ce46a9468da1fc44a5851e2(requestParameters: 5a5298550ce46a9468da1fc44a5851e2Request, initOverrides?: RequestInit | runtime.InitOverrideFunction): Promise<void> {
        await this._5a5298550ce46a9468da1fc44a5851e2Raw(requestParameters, initOverrides);
    }

    /**
     * Create Point
     * createPoint
     */
    async _7b9b6b981e45df08007c3f1a42a57595Raw(requestParameters: 7b9b6b981e45df08007c3f1a42a57595Request, initOverrides?: RequestInit | runtime.InitOverrideFunction): Promise<runtime.ApiResponse<void>> {
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
            path: `/points`,
            method: 'POST',
            headers: headerParameters,
            query: queryParameters,
            body: formParams,
        }, initOverrides);

        return new runtime.VoidApiResponse(response);
    }

    /**
     * Create Point
     * createPoint
     */
    async _7b9b6b981e45df08007c3f1a42a57595(requestParameters: 7b9b6b981e45df08007c3f1a42a57595Request = {}, initOverrides?: RequestInit | runtime.InitOverrideFunction): Promise<void> {
        await this._7b9b6b981e45df08007c3f1a42a57595Raw(requestParameters, initOverrides);
    }

    /**
     * Delete Point
     * deletePoint
     */
    async _8382eb01398d1029539a633a18910721Raw(requestParameters: 8382eb01398d1029539a633a18910721Request, initOverrides?: RequestInit | runtime.InitOverrideFunction): Promise<runtime.ApiResponse<void>> {
        if (requestParameters.id === null || requestParameters.id === undefined) {
            throw new runtime.RequiredError('id','Required parameter requestParameters.id was null or undefined when calling _8382eb01398d1029539a633a18910721.');
        }

        const queryParameters: any = {};

        const headerParameters: runtime.HTTPHeaders = {};

        const response = await this.request({
            path: `/points/{id}`.replace(`{${"id"}}`, encodeURIComponent(String(requestParameters.id))),
            method: 'DELETE',
            headers: headerParameters,
            query: queryParameters,
        }, initOverrides);

        return new runtime.VoidApiResponse(response);
    }

    /**
     * Delete Point
     * deletePoint
     */
    async _8382eb01398d1029539a633a18910721(requestParameters: 8382eb01398d1029539a633a18910721Request, initOverrides?: RequestInit | runtime.InitOverrideFunction): Promise<void> {
        await this._8382eb01398d1029539a633a18910721Raw(requestParameters, initOverrides);
    }

    /**
     * Get Point
     * getPointItem
     */
    async _84890aa276d26faec598361de21ed829Raw(requestParameters: 84890aa276d26faec598361de21ed829Request, initOverrides?: RequestInit | runtime.InitOverrideFunction): Promise<runtime.ApiResponse<void>> {
        if (requestParameters.id === null || requestParameters.id === undefined) {
            throw new runtime.RequiredError('id','Required parameter requestParameters.id was null or undefined when calling _84890aa276d26faec598361de21ed829.');
        }

        const queryParameters: any = {};

        const headerParameters: runtime.HTTPHeaders = {};

        const response = await this.request({
            path: `/points/{id}`.replace(`{${"id"}}`, encodeURIComponent(String(requestParameters.id))),
            method: 'GET',
            headers: headerParameters,
            query: queryParameters,
        }, initOverrides);

        return new runtime.VoidApiResponse(response);
    }

    /**
     * Get Point
     * getPointItem
     */
    async _84890aa276d26faec598361de21ed829(requestParameters: 84890aa276d26faec598361de21ed829Request, initOverrides?: RequestInit | runtime.InitOverrideFunction): Promise<void> {
        await this._84890aa276d26faec598361de21ed829Raw(requestParameters, initOverrides);
    }

}
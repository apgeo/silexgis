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

export interface 2458cebcb928d0cf634acaf03b114ba0Request {
    id: number;
}

export interface 3b7d4d912acf1978ce1f451c27525b85Request {
    name?: string;
}

export interface A395e23edaa4ccfce651904bf97ff304Request {
    id: number;
    name?: string;
}

export interface B0be1724d2b4030ef83d5fe144802562Request {
    id: number;
}

/**
 * 
 */
export class TripLogsApi extends runtime.BaseAPI {

    /**
     * Delete TripLogs
     * deleteTripLogs
     */
    async _2458cebcb928d0cf634acaf03b114ba0Raw(requestParameters: 2458cebcb928d0cf634acaf03b114ba0Request, initOverrides?: RequestInit | runtime.InitOverrideFunction): Promise<runtime.ApiResponse<void>> {
        if (requestParameters.id === null || requestParameters.id === undefined) {
            throw new runtime.RequiredError('id','Required parameter requestParameters.id was null or undefined when calling _2458cebcb928d0cf634acaf03b114ba0.');
        }

        const queryParameters: any = {};

        const headerParameters: runtime.HTTPHeaders = {};

        const response = await this.request({
            path: `/tripLogs/{id}`.replace(`{${"id"}}`, encodeURIComponent(String(requestParameters.id))),
            method: 'DELETE',
            headers: headerParameters,
            query: queryParameters,
        }, initOverrides);

        return new runtime.VoidApiResponse(response);
    }

    /**
     * Delete TripLogs
     * deleteTripLogs
     */
    async _2458cebcb928d0cf634acaf03b114ba0(requestParameters: 2458cebcb928d0cf634acaf03b114ba0Request, initOverrides?: RequestInit | runtime.InitOverrideFunction): Promise<void> {
        await this._2458cebcb928d0cf634acaf03b114ba0Raw(requestParameters, initOverrides);
    }

    /**
     * Create TripLogs
     * createTripLogs
     */
    async _3b7d4d912acf1978ce1f451c27525b85Raw(requestParameters: 3b7d4d912acf1978ce1f451c27525b85Request, initOverrides?: RequestInit | runtime.InitOverrideFunction): Promise<runtime.ApiResponse<void>> {
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
            path: `/tripLogs`,
            method: 'POST',
            headers: headerParameters,
            query: queryParameters,
            body: formParams,
        }, initOverrides);

        return new runtime.VoidApiResponse(response);
    }

    /**
     * Create TripLogs
     * createTripLogs
     */
    async _3b7d4d912acf1978ce1f451c27525b85(requestParameters: 3b7d4d912acf1978ce1f451c27525b85Request = {}, initOverrides?: RequestInit | runtime.InitOverrideFunction): Promise<void> {
        await this._3b7d4d912acf1978ce1f451c27525b85Raw(requestParameters, initOverrides);
    }

    /**
     * Update TripLogs
     * updateTripLogs
     */
    async a395e23edaa4ccfce651904bf97ff304Raw(requestParameters: A395e23edaa4ccfce651904bf97ff304Request, initOverrides?: RequestInit | runtime.InitOverrideFunction): Promise<runtime.ApiResponse<void>> {
        if (requestParameters.id === null || requestParameters.id === undefined) {
            throw new runtime.RequiredError('id','Required parameter requestParameters.id was null or undefined when calling a395e23edaa4ccfce651904bf97ff304.');
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
            path: `/tripLogs/{id}`.replace(`{${"id"}}`, encodeURIComponent(String(requestParameters.id))),
            method: 'PUT',
            headers: headerParameters,
            query: queryParameters,
            body: formParams,
        }, initOverrides);

        return new runtime.VoidApiResponse(response);
    }

    /**
     * Update TripLogs
     * updateTripLogs
     */
    async a395e23edaa4ccfce651904bf97ff304(requestParameters: A395e23edaa4ccfce651904bf97ff304Request, initOverrides?: RequestInit | runtime.InitOverrideFunction): Promise<void> {
        await this.a395e23edaa4ccfce651904bf97ff304Raw(requestParameters, initOverrides);
    }

    /**
     * Get TripLogs
     * getTripLogsItem
     */
    async b0be1724d2b4030ef83d5fe144802562Raw(requestParameters: B0be1724d2b4030ef83d5fe144802562Request, initOverrides?: RequestInit | runtime.InitOverrideFunction): Promise<runtime.ApiResponse<void>> {
        if (requestParameters.id === null || requestParameters.id === undefined) {
            throw new runtime.RequiredError('id','Required parameter requestParameters.id was null or undefined when calling b0be1724d2b4030ef83d5fe144802562.');
        }

        const queryParameters: any = {};

        const headerParameters: runtime.HTTPHeaders = {};

        const response = await this.request({
            path: `/tripLogs/{id}`.replace(`{${"id"}}`, encodeURIComponent(String(requestParameters.id))),
            method: 'GET',
            headers: headerParameters,
            query: queryParameters,
        }, initOverrides);

        return new runtime.VoidApiResponse(response);
    }

    /**
     * Get TripLogs
     * getTripLogsItem
     */
    async b0be1724d2b4030ef83d5fe144802562(requestParameters: B0be1724d2b4030ef83d5fe144802562Request, initOverrides?: RequestInit | runtime.InitOverrideFunction): Promise<void> {
        await this.b0be1724d2b4030ef83d5fe144802562Raw(requestParameters, initOverrides);
    }

    /**
     * Get all TripLogs
     * getTripLogsList
     */
    async c15a7f9a6008ff7e48676ca3cf804d45Raw(initOverrides?: RequestInit | runtime.InitOverrideFunction): Promise<runtime.ApiResponse<void>> {
        const queryParameters: any = {};

        const headerParameters: runtime.HTTPHeaders = {};

        const response = await this.request({
            path: `/tripLogs`,
            method: 'GET',
            headers: headerParameters,
            query: queryParameters,
        }, initOverrides);

        return new runtime.VoidApiResponse(response);
    }

    /**
     * Get all TripLogs
     * getTripLogsList
     */
    async c15a7f9a6008ff7e48676ca3cf804d45(initOverrides?: RequestInit | runtime.InitOverrideFunction): Promise<void> {
        await this.c15a7f9a6008ff7e48676ca3cf804d45Raw(initOverrides);
    }

}

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

export interface 3dc956fb0ff8265eb54d1a6d37cd98f5Request {
    id: number;
    name?: string;
}

export interface B17ec9ec45bbc639e95da6411336384bRequest {
    name?: string;
}

export interface D20b3d23666dc5faa14e9240433e2e86Request {
    id: number;
}

export interface Fb6ff65241b7d3313109e3ffa0fe0b8aRequest {
    id: number;
}

/**
 * 
 */
export class FileApi extends runtime.BaseAPI {

    /**
     * Get all Files
     * getFileList
     */
    async _22fbfeda8d53a88acbdbe16ab3e86b8fRaw(initOverrides?: RequestInit | runtime.InitOverrideFunction): Promise<runtime.ApiResponse<void>> {
        const queryParameters: any = {};

        const headerParameters: runtime.HTTPHeaders = {};

        const response = await this.request({
            path: `/files`,
            method: 'GET',
            headers: headerParameters,
            query: queryParameters,
        }, initOverrides);

        return new runtime.VoidApiResponse(response);
    }

    /**
     * Get all Files
     * getFileList
     */
    async _22fbfeda8d53a88acbdbe16ab3e86b8f(initOverrides?: RequestInit | runtime.InitOverrideFunction): Promise<void> {
        await this._22fbfeda8d53a88acbdbe16ab3e86b8fRaw(initOverrides);
    }

    /**
     * Update File
     * updateFile
     */
    async _3dc956fb0ff8265eb54d1a6d37cd98f5Raw(requestParameters: 3dc956fb0ff8265eb54d1a6d37cd98f5Request, initOverrides?: RequestInit | runtime.InitOverrideFunction): Promise<runtime.ApiResponse<void>> {
        if (requestParameters.id === null || requestParameters.id === undefined) {
            throw new runtime.RequiredError('id','Required parameter requestParameters.id was null or undefined when calling _3dc956fb0ff8265eb54d1a6d37cd98f5.');
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
            path: `/files/{id}`.replace(`{${"id"}}`, encodeURIComponent(String(requestParameters.id))),
            method: 'PUT',
            headers: headerParameters,
            query: queryParameters,
            body: formParams,
        }, initOverrides);

        return new runtime.VoidApiResponse(response);
    }

    /**
     * Update File
     * updateFile
     */
    async _3dc956fb0ff8265eb54d1a6d37cd98f5(requestParameters: 3dc956fb0ff8265eb54d1a6d37cd98f5Request, initOverrides?: RequestInit | runtime.InitOverrideFunction): Promise<void> {
        await this._3dc956fb0ff8265eb54d1a6d37cd98f5Raw(requestParameters, initOverrides);
    }

    /**
     * Create File
     * createFile
     */
    async b17ec9ec45bbc639e95da6411336384bRaw(requestParameters: B17ec9ec45bbc639e95da6411336384bRequest, initOverrides?: RequestInit | runtime.InitOverrideFunction): Promise<runtime.ApiResponse<void>> {
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
            path: `/files`,
            method: 'POST',
            headers: headerParameters,
            query: queryParameters,
            body: formParams,
        }, initOverrides);

        return new runtime.VoidApiResponse(response);
    }

    /**
     * Create File
     * createFile
     */
    async b17ec9ec45bbc639e95da6411336384b(requestParameters: B17ec9ec45bbc639e95da6411336384bRequest = {}, initOverrides?: RequestInit | runtime.InitOverrideFunction): Promise<void> {
        await this.b17ec9ec45bbc639e95da6411336384bRaw(requestParameters, initOverrides);
    }

    /**
     * Get File
     * getFileItem
     */
    async d20b3d23666dc5faa14e9240433e2e86Raw(requestParameters: D20b3d23666dc5faa14e9240433e2e86Request, initOverrides?: RequestInit | runtime.InitOverrideFunction): Promise<runtime.ApiResponse<void>> {
        if (requestParameters.id === null || requestParameters.id === undefined) {
            throw new runtime.RequiredError('id','Required parameter requestParameters.id was null or undefined when calling d20b3d23666dc5faa14e9240433e2e86.');
        }

        const queryParameters: any = {};

        const headerParameters: runtime.HTTPHeaders = {};

        const response = await this.request({
            path: `/files/{id}`.replace(`{${"id"}}`, encodeURIComponent(String(requestParameters.id))),
            method: 'GET',
            headers: headerParameters,
            query: queryParameters,
        }, initOverrides);

        return new runtime.VoidApiResponse(response);
    }

    /**
     * Get File
     * getFileItem
     */
    async d20b3d23666dc5faa14e9240433e2e86(requestParameters: D20b3d23666dc5faa14e9240433e2e86Request, initOverrides?: RequestInit | runtime.InitOverrideFunction): Promise<void> {
        await this.d20b3d23666dc5faa14e9240433e2e86Raw(requestParameters, initOverrides);
    }

    /**
     * Delete File
     * deleteFile
     */
    async fb6ff65241b7d3313109e3ffa0fe0b8aRaw(requestParameters: Fb6ff65241b7d3313109e3ffa0fe0b8aRequest, initOverrides?: RequestInit | runtime.InitOverrideFunction): Promise<runtime.ApiResponse<void>> {
        if (requestParameters.id === null || requestParameters.id === undefined) {
            throw new runtime.RequiredError('id','Required parameter requestParameters.id was null or undefined when calling fb6ff65241b7d3313109e3ffa0fe0b8a.');
        }

        const queryParameters: any = {};

        const headerParameters: runtime.HTTPHeaders = {};

        const response = await this.request({
            path: `/files/{id}`.replace(`{${"id"}}`, encodeURIComponent(String(requestParameters.id))),
            method: 'DELETE',
            headers: headerParameters,
            query: queryParameters,
        }, initOverrides);

        return new runtime.VoidApiResponse(response);
    }

    /**
     * Delete File
     * deleteFile
     */
    async fb6ff65241b7d3313109e3ffa0fe0b8a(requestParameters: Fb6ff65241b7d3313109e3ffa0fe0b8aRequest, initOverrides?: RequestInit | runtime.InitOverrideFunction): Promise<void> {
        await this.fb6ff65241b7d3313109e3ffa0fe0b8aRaw(requestParameters, initOverrides);
    }

}
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

export interface 5183e00186551512bbeb06d3d6d156cdRequest {
    id: number;
}

export interface 63bb1b2694a4134e1e5798c7a683a3ccRequest {
    id: number;
    name?: string;
}

export interface 91c6b95c1b6104ea3fc0a7f1f85abafeRequest {
    name?: string;
}

export interface E76e9b25359179f7558a7309abaa8bd1Request {
    id: number;
}

/**
 * 
 */
export class ImageApi extends runtime.BaseAPI {

    /**
     * Delete Image
     * deleteImage
     */
    async _5183e00186551512bbeb06d3d6d156cdRaw(requestParameters: 5183e00186551512bbeb06d3d6d156cdRequest, initOverrides?: RequestInit | runtime.InitOverrideFunction): Promise<runtime.ApiResponse<void>> {
        if (requestParameters.id === null || requestParameters.id === undefined) {
            throw new runtime.RequiredError('id','Required parameter requestParameters.id was null or undefined when calling _5183e00186551512bbeb06d3d6d156cd.');
        }

        const queryParameters: any = {};

        const headerParameters: runtime.HTTPHeaders = {};

        const response = await this.request({
            path: `/images/{id}`.replace(`{${"id"}}`, encodeURIComponent(String(requestParameters.id))),
            method: 'DELETE',
            headers: headerParameters,
            query: queryParameters,
        }, initOverrides);

        return new runtime.VoidApiResponse(response);
    }

    /**
     * Delete Image
     * deleteImage
     */
    async _5183e00186551512bbeb06d3d6d156cd(requestParameters: 5183e00186551512bbeb06d3d6d156cdRequest, initOverrides?: RequestInit | runtime.InitOverrideFunction): Promise<void> {
        await this._5183e00186551512bbeb06d3d6d156cdRaw(requestParameters, initOverrides);
    }

    /**
     * Update Image
     * updateImage
     */
    async _63bb1b2694a4134e1e5798c7a683a3ccRaw(requestParameters: 63bb1b2694a4134e1e5798c7a683a3ccRequest, initOverrides?: RequestInit | runtime.InitOverrideFunction): Promise<runtime.ApiResponse<void>> {
        if (requestParameters.id === null || requestParameters.id === undefined) {
            throw new runtime.RequiredError('id','Required parameter requestParameters.id was null or undefined when calling _63bb1b2694a4134e1e5798c7a683a3cc.');
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
            path: `/images/{id}`.replace(`{${"id"}}`, encodeURIComponent(String(requestParameters.id))),
            method: 'PUT',
            headers: headerParameters,
            query: queryParameters,
            body: formParams,
        }, initOverrides);

        return new runtime.VoidApiResponse(response);
    }

    /**
     * Update Image
     * updateImage
     */
    async _63bb1b2694a4134e1e5798c7a683a3cc(requestParameters: 63bb1b2694a4134e1e5798c7a683a3ccRequest, initOverrides?: RequestInit | runtime.InitOverrideFunction): Promise<void> {
        await this._63bb1b2694a4134e1e5798c7a683a3ccRaw(requestParameters, initOverrides);
    }

    /**
     * Get all Images
     * getImageList
     */
    async _8664187b703ad558537c9d91449eed7aRaw(initOverrides?: RequestInit | runtime.InitOverrideFunction): Promise<runtime.ApiResponse<void>> {
        const queryParameters: any = {};

        const headerParameters: runtime.HTTPHeaders = {};

        const response = await this.request({
            path: `/images`,
            method: 'GET',
            headers: headerParameters,
            query: queryParameters,
        }, initOverrides);

        return new runtime.VoidApiResponse(response);
    }

    /**
     * Get all Images
     * getImageList
     */
    async _8664187b703ad558537c9d91449eed7a(initOverrides?: RequestInit | runtime.InitOverrideFunction): Promise<void> {
        await this._8664187b703ad558537c9d91449eed7aRaw(initOverrides);
    }

    /**
     * Create Image
     * createImage
     */
    async _91c6b95c1b6104ea3fc0a7f1f85abafeRaw(requestParameters: 91c6b95c1b6104ea3fc0a7f1f85abafeRequest, initOverrides?: RequestInit | runtime.InitOverrideFunction): Promise<runtime.ApiResponse<void>> {
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
            path: `/images`,
            method: 'POST',
            headers: headerParameters,
            query: queryParameters,
            body: formParams,
        }, initOverrides);

        return new runtime.VoidApiResponse(response);
    }

    /**
     * Create Image
     * createImage
     */
    async _91c6b95c1b6104ea3fc0a7f1f85abafe(requestParameters: 91c6b95c1b6104ea3fc0a7f1f85abafeRequest = {}, initOverrides?: RequestInit | runtime.InitOverrideFunction): Promise<void> {
        await this._91c6b95c1b6104ea3fc0a7f1f85abafeRaw(requestParameters, initOverrides);
    }

    /**
     * Get Image
     * getImageItem
     */
    async e76e9b25359179f7558a7309abaa8bd1Raw(requestParameters: E76e9b25359179f7558a7309abaa8bd1Request, initOverrides?: RequestInit | runtime.InitOverrideFunction): Promise<runtime.ApiResponse<void>> {
        if (requestParameters.id === null || requestParameters.id === undefined) {
            throw new runtime.RequiredError('id','Required parameter requestParameters.id was null or undefined when calling e76e9b25359179f7558a7309abaa8bd1.');
        }

        const queryParameters: any = {};

        const headerParameters: runtime.HTTPHeaders = {};

        const response = await this.request({
            path: `/images/{id}`.replace(`{${"id"}}`, encodeURIComponent(String(requestParameters.id))),
            method: 'GET',
            headers: headerParameters,
            query: queryParameters,
        }, initOverrides);

        return new runtime.VoidApiResponse(response);
    }

    /**
     * Get Image
     * getImageItem
     */
    async e76e9b25359179f7558a7309abaa8bd1(requestParameters: E76e9b25359179f7558a7309abaa8bd1Request, initOverrides?: RequestInit | runtime.InitOverrideFunction): Promise<void> {
        await this.e76e9b25359179f7558a7309abaa8bd1Raw(requestParameters, initOverrides);
    }

}

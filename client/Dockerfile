# SPDX-License-Identifier: AGPL-3.0-or-later
FROM node:22-alpine AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
RUN npm run build

FROM nginx:alpine

# The configuration is installed as a TEMPLATE, not as a finished file.
#
# One thing in it has to be decided by whoever runs the installation rather than by whoever built
# the image: which sites may frame a published trip. The base image's entrypoint expands
# `/etc/nginx/templates/*.template` into the served configuration at start-up, which is how that
# one value gets in without an operator editing a file inside an image.
#
# <b>The filter is load-bearing.</b> Left unset, the expansion substitutes EVERY variable it finds
# in the file — and this configuration is full of nginx's own: `$uri`, `$scheme`, `$http_host`,
# `$proxy_add_x_forwarded_for`, `$terrain_build_cache`. All of them would be replaced by the empty
# string, and what comes out is a configuration that is still valid, still starts, and serves
# everything wrongly. Naming a prefix here means nothing but this setting is touched.
ENV NGINX_ENVSUBST_FILTER="^SILEXGIS__Web__"
# Declared empty so the expansion has something to put in. A variable that is not set at all is
# left in the file verbatim, and the header would then go out quoting its own placeholder.
ENV SILEXGIS__Web__FrameAncestors=""
COPY nginx.conf /etc/nginx/templates/default.conf.template
COPY --from=build /app/dist /usr/share/nginx/html
EXPOSE 80

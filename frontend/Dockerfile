# One image definition for both web apps: APP=frontend (the tenant app) or APP=platform-web
# (landing page, signup and staff console). Each has its own nginx site, proxying /api to its API.
FROM node:22-alpine AS build
ARG APP=frontend
WORKDIR /app
COPY package*.json ./
RUN npm ci --no-audit --no-fund
COPY . .
RUN npx ng build ${APP} --configuration production
FROM nginx:alpine
ARG APP=frontend
COPY nginx.${APP}.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist/${APP}/browser /usr/share/nginx/html

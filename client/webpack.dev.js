const path = require('path');
const { merge } = require('webpack-merge');
const ReactRefreshWebpackPlugin = require('@pmmmwh/react-refresh-webpack-plugin');
const HtmlWebpackPlugin = require('html-webpack-plugin');

const common = require('./webpack.common.js');

module.exports = merge(common, {
  mode: 'development',
  devtool: 'inline-source-map',
  devServer: {
    server: 'https',
    hot: true,
    static: path.join(__dirname, 'resources', 'public')
  },
  plugins: [
    new ReactRefreshWebpackPlugin()
  ],

  // plugins: [
  //   new HtmlWebpackPlugin({
  //     template: 'src/index.html'
  //   })
  // ],
  entry: './src/index.tsx',
  output: {
    path: path.resolve(__dirname, 'dist'),
    filename: 'index.js',
    publicPath: '/'
  },
  devServer: {
    historyApiFallback: true,
  },
  // module: {
  //   rules: [
  //     { test: /\.(js)$/, use: 'babel-loader' },
  //     { test: /\.css$/, use: [ 'style-loader', 'css-loader' ]}
  //   ]
  // },
});

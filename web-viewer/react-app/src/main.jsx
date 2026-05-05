import React from 'react'
import ReactDOM from 'react-dom/client'
import maplibregl from 'maplibre-gl'
import { Protocol } from 'pmtiles'
import App from './App'
import './index.css'

const protocol = new Protocol()
maplibregl.addProtocol('pmtiles', protocol.tile.bind(protocol))

ReactDOM.createRoot(document.getElementById('root')).render(
  <React.StrictMode>
    <App />
  </React.StrictMode>
)

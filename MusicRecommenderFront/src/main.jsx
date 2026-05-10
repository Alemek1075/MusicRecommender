import React from 'react'
import ReactDOM from 'react-dom/client'
import { BrowserRouter } from 'react-router-dom'
import App from './App'
import './index.css'

// React entry point. BrowserRouter enables the route table in App.jsx, and StrictMode helps catch
// unsafe React patterns during development.
ReactDOM.createRoot(document.getElementById('root')).render(
  /* StrictMode intentionally double-invokes some development lifecycles to catch unsafe logic. */
  <React.StrictMode>
    {/* BrowserRouter provides URL/history state for React Router. */}
    <BrowserRouter>
      {/* App contains the route table and persistent shell. */}
      <App />
    </BrowserRouter>
  </React.StrictMode>
)

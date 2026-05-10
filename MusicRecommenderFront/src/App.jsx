import { Routes, Route, Navigate } from 'react-router-dom'
import Navbar from './components/Navbar'
import Home from './pages/Home'
import Playlists from './pages/Playlists'
import PlaylistDetail from './pages/PlaylistDetail'
import History from './pages/History'
import { ImportProvider } from './context/ImportContext'

/**
 * Top-level application shell. It keeps the persistent navigation visible, constrains page width,
 * and defines the client-side routes rendered inside the shared import-state provider.
 */
export default function App() {
  return (
    /* ImportProvider lets the navbar react to long-running playlist imports. */
    <ImportProvider>
      {/* Dark full-height app background. */}
      <div className="min-h-screen" style={{ backgroundColor: '#0b0d17' }}>
        {/* Persistent navigation across all routes. */}
        <Navbar />
        {/* Shared page width and vertical padding. */}
        <main className="max-w-5xl mx-auto px-4 py-10">
          {/* Client-side route table for each top-level app page. */}
          <Routes>
            <Route path="/" element={<Home />} />
            <Route path="/playlists" element={<Playlists />} />
            <Route path="/playlists/:id" element={<PlaylistDetail />} />
            <Route path="/history" element={<History />} />
            {/* Unknown routes return the user to the homepage. */}
            <Route path="*" element={<Navigate to="/" replace />} />
          </Routes>
        </main>
      </div>
    </ImportProvider>
  )
}

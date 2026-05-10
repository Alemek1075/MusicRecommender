import { createContext, useContext, useState } from 'react'

// Shared import-state context. The navigation uses this flag to prevent route changes while a
// playlist is being analyzed, which avoids losing the modal's AbortController and progress state.
const ImportContext = createContext({ isImporting: false, setIsImporting: () => {} })

/**
 * Provides global playlist-import state to pages and the navbar.
 */
export function ImportProvider({ children }) {
  // Tracks whether any import modal/form currently has a request in flight.
  const [isImporting, setIsImporting] = useState(false)

  // Expose both the value and setter so nested pages can lock/unlock navigation.
  return (
    <ImportContext.Provider value={{ isImporting, setIsImporting }}>
      {children}
    </ImportContext.Provider>
  )
}

/**
 * Convenience hook for reading/updating whether the app is currently importing a playlist.
 */
export function useImport() {
  // Consumers use this instead of importing the context directly.
  return useContext(ImportContext)
}

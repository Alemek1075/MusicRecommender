import { createContext, useContext, useState } from 'react'

const ImportContext = createContext({ isImporting: false, setIsImporting: () => {} })

export function ImportProvider({ children }) {
  const [isImporting, setIsImporting] = useState(false)
  return (
    <ImportContext.Provider value={{ isImporting, setIsImporting }}>
      {children}
    </ImportContext.Provider>
  )
}

export function useImport() {
  return useContext(ImportContext)
}

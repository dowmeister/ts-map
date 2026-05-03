import { useState, useEffect } from 'react'

const ROUTING_BASE = import.meta.env.VITE_ROUTING_SERVICE_URL || 'http://localhost:3001'

export function useCompanies(game) {
  const [companies, setCompanies] = useState([])

  useEffect(() => {
    if (!game) return
    setCompanies([])
    fetch(`${ROUTING_BASE}/api/companies?game=${game}`)
      .then(r => r.json())
      .then(data => setCompanies(data.companies || []))
      .catch(() => setCompanies([]))
  }, [game])

  return companies
}

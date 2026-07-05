import { useEffect, useState } from 'react'
import { Image, Center, Loader } from '@mantine/core'
import { Brand, brandNames } from '../types'

const brandImages: Record<Brand, string[]> = {
  [Brand.Siemens]: [
    'https://symbols-electrical.getvecta.com/stencil_368/10_siemens-simatic-s7-1500-cpu-1518-4-pn-dp-front-view.9e660bc458.svg',
    'https://symbols-electrical.getvecta.com/stencil_367/5_siemens-simatic-s7-1500-cpu-1518-4-pn-dp.99579f1def.svg',
  ],
  [Brand.Rockwell]: [
    'data:image/svg+xml,' + encodeURIComponent('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 180 100"><rect width="180" height="100" rx="6" fill="#e9ecef" stroke="#c2255c" stroke-width="1.5"/><rect x="10" y="10" width="160" height="30" rx="3" fill="#c2255c"/><text x="90" y="30" text-anchor="middle" font-family="Arial" font-size="11" font-weight="bold" fill="#fff">Allen-Bradley</text><text x="90" y="48" text-anchor="middle" font-family="Arial" font-size="8" fill="#c2255c">ControlLogix 1756-L7x</text><rect x="15" y="55" width="30" height="20" rx="2" fill="#fff" stroke="#c2255c" stroke-width="1"/><rect x="50" y="55" width="30" height="20" rx="2" fill="#fff" stroke="#c2255c" stroke-width="1"/><rect x="85" y="55" width="30" height="20" rx="2" fill="#fff" stroke="#c2255c" stroke-width="1"/><rect x="120" y="55" width="30" height="20" rx="2" fill="#fff" stroke="#c2255c" stroke-width="1"/><circle cx="30" cy="65" r="4" fill="#40c057"/><circle cx="65" cy="65" r="4" fill="#fab005"/><circle cx="100" cy="65" r="4" fill="#228be6"/><circle cx="135" cy="65" r="4" fill="#fa5252"/><text x="90" y="90" text-anchor="middle" font-family="monospace" font-size="7" fill="#868e96">EtherNet/IP</text></svg>'),
  ],
  [Brand.Modbus]: [
    'data:image/svg+xml,' + encodeURIComponent('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 180 100"><rect width="180" height="100" rx="6" fill="#e9ecef" stroke="#2f9e44" stroke-width="1.5"/><rect x="10" y="10" width="160" height="30" rx="3" fill="#2f9e44"/><text x="90" y="30" text-anchor="middle" font-family="Arial" font-size="11" font-weight="bold" fill="#fff">Schneider Electric</text><text x="90" y="48" text-anchor="middle" font-family="Arial" font-size="8" fill="#2f9e44">Modicon M340</text><rect x="15" y="55" width="40" height="20" rx="2" fill="#fff" stroke="#2f9e44" stroke-width="1"/><rect x="65" y="55" width="40" height="20" rx="2" fill="#fff" stroke="#2f9e44" stroke-width="1"/><rect x="115" y="55" width="40" height="20" rx="2" fill="#fff" stroke="#2f9e44" stroke-width="1"/><circle cx="35" cy="65" r="4" fill="#40c057"/><circle cx="85" cy="65" r="4" fill="#fab005"/><circle cx="135" cy="65" r="4" fill="#228be6"/><text x="90" y="90" text-anchor="middle" font-family="monospace" font-size="7" fill="#868e96">Modbus TCP</text></svg>'),
  ],
  [Brand.Mitsubishi]: [
    'https://symbols-electrical.getvecta.com/stencil_615/10_mitsubishi-melsec-iq-f-plc-fx5u-80mt-es-cpu-80i-o-front-view.df4ce1c949.svg',
  ],
  [Brand.Beckhoff]: [
    'https://symbols-electrical.getvecta.com/stencil_262/13_beckhoff-icon.2770753a6f.svg',
  ],
  [Brand.OpcUa]: [
    'data:image/svg+xml,' + encodeURIComponent('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 180 100"><rect width="180" height="100" rx="6" fill="#e9ecef" stroke="#15aabf" stroke-width="1.5"/><rect x="10" y="10" width="160" height="30" rx="3" fill="#15aabf"/><text x="90" y="30" text-anchor="middle" font-family="Arial" font-size="11" font-weight="bold" fill="#fff">OPC UA</text><text x="90" y="48" text-anchor="middle" font-family="Arial" font-size="8" fill="#15aabf">Unified Architecture</text><rect x="15" y="55" width="70" height="20" rx="2" fill="#fff" stroke="#15aabf" stroke-width="1"/><rect x="95" y="55" width="70" height="20" rx="2" fill="#fff" stroke="#15aabf" stroke-width="1"/><circle cx="50" cy="65" r="4" fill="#40c057"/><circle cx="130" cy="65" r="4" fill="#228be6"/><text x="90" y="90" text-anchor="middle" font-family="monospace" font-size="7" fill="#868e96">OPC UA TCP</text></svg>'),
  ],
}

interface PlcBrandImageProps {
  brand: Brand
  state: number
  height?: number
}

export default function PlcBrandImage({ brand, state, height = 80 }: PlcBrandImageProps) {
  const urls = brandImages[brand] ?? []
  const [index, setIndex] = useState(0)

  useEffect(() => {
    if (urls.length <= 1) return
    const t = setInterval(() => setIndex(i => (i + 1) % urls.length), 4000)
    return () => clearInterval(t)
  }, [urls.length])

  return (
    <div style={{ position: 'relative', display: 'inline-block' }}>
      <Image
        src={urls[index] ?? urls[0]}
        alt={brandNames[brand]}
        h={height}
        fit="contain"
        fallbackSrc="data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 100 60'%3E%3Crect width='100' height='60' rx='4' fill='%23e9ecef' stroke='%23868e96' stroke-width='1'/%3E%3Ctext x='50' y='24' text-anchor='middle' font-family='monospace' font-size='10' fill='%23495057'%3EPLC%3C/text%3E%3C/svg%3E"
        style={{ opacity: state === 1 ? 1 : 0.5, transition: 'opacity 0.5s' }}
      />
      <div style={{ position: 'absolute', top: 4, right: 4, display: 'flex', gap: 3 }}>
        <div style={{ width: 8, height: 8, borderRadius: '50%', backgroundColor: state === 1 ? '#40c057' : state === 3 ? '#fa5252' : '#fab005', boxShadow: state === 1 ? '0 0 6px #40c057' : 'none', animation: state === 1 ? 'pulse 1.5s infinite' : 'none' }} />
        <div style={{ width: 8, height: 8, borderRadius: '50%', backgroundColor: state === 1 ? '#40c057' : '#868e96', animation: state === 1 ? 'pulse 1.5s infinite 0.3s' : 'none' }} />
      </div>
      <style>{`
        @keyframes pulse {
          0%, 100% { opacity: 1; transform: scale(1); }
          50% { opacity: 0.6; transform: scale(0.85); }
        }
      `}</style>
    </div>
  )
}

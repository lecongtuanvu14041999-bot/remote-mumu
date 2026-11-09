import React, { useEffect, useRef, useState } from 'react'
import { createRoot } from 'react-dom/client'
import * as signalR from '@microsoft/signalr'
import { Copy, Gamepad2, Link2, LogOut, MonitorUp, Send, Users } from 'lucide-react'
import './styles.css'
import './api-errors.css'

const API_URL = (import.meta.env.VITE_API_URL || 'https://remote-mumu.onrender.com').replace(/\/+$/, '')

interface Chat { name: string; text: string }

declare global {
  interface Window {
    google?: {
      accounts: {
        id: {
          initialize: (options: {
            client_id: string
            callback: (response: { credential: string }) => void
            use_fedcm_for_button?: boolean
          }) => void
          renderButton: (element: HTMLElement, options: { theme?: string; size?: string; text?: string }) => void
          disableAutoSelect: () => void
        }
      }
    }
  }
}

interface GoogleUser { subject: string; email: string; name: string; picture: string }

function App() {
  const [name, setName] = useState('Bạn')
  const [room, setRoom] = useState('')
  const [joined, setJoined] = useState(false)
  const [chat, setChat] = useState<Chat[]>([])
  const [message, setMessage] = useState('')
  const [connected, setConnected] = useState(false)
  const [frame, setFrame] = useState<string | null>(null)
  const [apiError, setApiError] = useState('')
  const [user, setUser] = useState<GoogleUser | null>(null)
  const [devices, setDevices] = useState<{ deviceId: string; deviceName: string; online: boolean }[]>([])
  const [myDevices, setMyDevices] = useState<{ pcName: string; deviceId: string; deviceName: string; online: boolean }[]>([])
  const [connectorUrl, setConnectorUrl] = useState<string | null>(null)
  const [activeView, setActiveView] = useState<'devices' | 'play'>('devices')
  const [roomName, setRoomName] = useState('Phòng của tôi')
  const [myRooms, setMyRooms] = useState<{ roomId: string; name: string; devices: {deviceId:string;deviceName:string;thumbnailBase64?:string}[] }[]>([])
  const connection = useRef<signalR.HubConnection | null>(null)
  const googleButton = useRef<HTMLDivElement>(null)

  const createRoom = async () => {
    if (!user) return
    setApiError('')
    try {
      const res = await fetch(`${API_URL}/api/rooms`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ ownerSubject: user.subject, ownerName: user.name, name: roomName })
      })
      if (!res.ok) throw new Error(`Tạo phòng thất bại (HTTP ${res.status})`)
      const data = await res.json()
      setRoom(data.roomId)
      setDevices(data.devices ?? [])
      setMyRooms(items => [data, ...items.filter(item => item.roomId !== data.roomId)])
      setConnectorUrl(`${API_URL}/downloads/RemoteMumu-Agent.zip`)
    } catch (error) {
      setApiError(error instanceof Error ? error.message : 'Không thể tạo phòng.')
    }
  }

  const joinRoom = async (roomCode = room) => {
    const roomId = roomCode.trim().toUpperCase()
    if (!roomId) return
    setApiError('')
    let hub: signalR.HubConnection | null = null
    try {
      const roomResponse = await fetch(`${API_URL}/api/rooms/${roomId}`)
      if (!roomResponse.ok) throw new Error(`Không tìm thấy phòng (HTTP ${roomResponse.status})`)
      const roomData = await roomResponse.json()
      setDevices(roomData.devices ?? [])
      hub = new signalR.HubConnectionBuilder()
        .withUrl(`${API_URL}/hubs/room`)
        .withAutomaticReconnect()
        .build()
      hub.on('ChatReceived', (payload: { displayName: string; message: string }) =>
        setChat(items => [...items, { name: payload.displayName, text: payload.message }]))
      hub.on('FrameReceived', (payload: { imageBase64: string }) =>
        setFrame(`data:image/jpeg;base64,${payload.imageBase64}`))
      hub.onclose(() => setConnected(false))
      await hub.start()
      await hub.invoke('JoinRoom', roomId, name)
      connection.current = hub
      setRoom(roomId)
      setConnected(true)
      setJoined(true)
    } catch (error) {
      await hub?.stop()
      setApiError(error instanceof Error ? error.message : 'Không thể tham gia phòng.')
    }
  }

  const sendChat = async () => {
    if (!message.trim() || !connection.current) return
    await connection.current.invoke('SendChat', room, name, message.trim())
    setMessage('')
  }

  const pointOnFrame = (event: React.PointerEvent<HTMLImageElement>) => {
    const image = event.currentTarget
    const bounds = image.getBoundingClientRect()
    return {
      x: Math.round((event.clientX - bounds.left) / bounds.width * image.naturalWidth),
      y: Math.round((event.clientY - bounds.top) / bounds.height * image.naturalHeight)
    }
  }

  const startGesture = useRef<{x: number; y: number} | null>(null)

  const onPointerDown = (event: React.PointerEvent<HTMLImageElement>) => {
    event.currentTarget.setPointerCapture(event.pointerId)
    startGesture.current = pointOnFrame(event)
  }

  const onPointerUp = async (event: React.PointerEvent<HTMLImageElement>) => {
    if (!connection.current || !startGesture.current) return
    const end = pointOnFrame(event)
    const distance = Math.hypot(end.x - startGesture.current.x, end.y - startGesture.current.y)
    if (distance < 8) {
      void connection.current.send('SendControl', room, { type: 'tap', ...startGesture.current })
    } else {
      void connection.current.send('SendControl', room, {
        type: 'swipe',
        x: startGesture.current.x,
        y: startGesture.current.y,
        x2: end.x,
        y2: end.y,
        duration: 180
      })
    }
    startGesture.current = null
  }

  const logoutGoogle = () => {
    window.google?.accounts.id.disableAutoSelect()
    setUser(null)
    setMyDevices([])
    setApiError('')
    setJoined(false)
    setName('Bạn')
  }

  useEffect(() => {
    const initialize = () => {
      if (!window.google || !googleButton.current) return
      window.google.accounts.id.initialize({
        client_id: '413729404676-dp2369quefq6tv75abuiu28nlvk87253.apps.googleusercontent.com',
        use_fedcm_for_button: true,
        callback: async ({ credential }) => {
          setApiError('')
          try {
            const response = await fetch(`${API_URL}/api/auth/google`, {
              method: 'POST',
              headers: { 'Content-Type': 'application/json' },
              body: JSON.stringify({ idToken: credential })
            })
            if (!response.ok) throw new Error(`Đăng nhập máy chủ thất bại (HTTP ${response.status})`)
            const profile = await response.json() as GoogleUser
            setUser(profile)
            setName(profile.name)
            const connector = new URLSearchParams(window.location.search).get('connector')
            if (connector) {
              let delivered = false
              for (let attempt = 0; attempt < 3 && !delivered; attempt++) {
                try {
                  const callback = await fetch(connector, {
                    method: 'POST',
                    mode: 'cors',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ subject: profile.subject, name: profile.name, email: profile.email, picture: profile.picture })
                  })
                  delivered = callback.ok
                } catch {
                  await new Promise(resolve => window.setTimeout(resolve, 500))
                }
              }
              if (delivered) {
                window.close()
              } else {
                setApiError('Đã đăng nhập Google nhưng không kết nối được Desktop Connector. Hãy mở lại ứng dụng desktop và thử đăng nhập lại.')
              }
            }
          } catch (error) {
            setApiError(error instanceof Error ? error.message : 'Không thể đăng nhập.')
          }
        }
      })
      window.google.accounts.id.renderButton(googleButton.current, {
        theme: 'outline', size: 'large', text: 'signin_with'
      })
    }

    initialize()
    if (!window.google) {
      const interval = window.setInterval(() => {
        if (window.google) {
          window.clearInterval(interval)
          initialize()
        }
      }, 200)
      return () => window.clearInterval(interval)
    }
  }, [])

  useEffect(() => () => { void connection.current?.stop() }, [])

  useEffect(() => {
    if (!user) return
    let cancelled = false
    const loadDevices = async () => {
      try {
        const response = await fetch(`${API_URL}/api/devices/me/${encodeURIComponent(user.subject)}`)
        if (!response.ok) throw new Error(`HTTP ${response.status}`)
        const result = await response.json()
        if (!cancelled) setMyDevices(result)
      } catch {
        if (!cancelled) setApiError('Không thể tải thiết bị. Hãy kiểm tra kết nối máy chủ và Desktop Connector.')
      }
    }
    void loadDevices()
    const timer = window.setInterval(loadDevices, 5000)
    return () => { cancelled = true; window.clearInterval(timer) }
  }, [user])

  useEffect(() => {
    if (!user) { setMyRooms([]); return }
    let cancelled = false
    const loadRooms = async () => {
      try {
        const response = await fetch(`${API_URL}/api/rooms/me/${encodeURIComponent(user.subject)}`)
        if (!response.ok) throw new Error(`HTTP ${response.status}`)
        const rooms = await response.json()
        if (!cancelled) setMyRooms(rooms)
      } catch {
        if (!cancelled) setApiError('Không thể tải danh sách phòng. Vui lòng thử tải lại trang.')
      }
    }
    void loadRooms()
    const timer = window.setInterval(loadRooms, 5000)
    return () => { cancelled = true; window.clearInterval(timer) }
  }, [user])

  return <main>
    {!joined ? <section className="landing card">
      <div className="brand"><Gamepad2/><b>Remote Mumu</b></div>
      <h1>Điều khiển Mumu từ xa</h1>
      <p className="subtitle">Quản lý PC/emulator của bạn và chia sẻ room với bạn bè.</p>
      <div className="google-login" ref={googleButton}></div>
      {apiError && <p className="api-error" role="alert">{apiError}</p>}
      {user && <div className="profile-row"><span><b>{user.name}</b><small>{user.email}</small></span><button className="logout-button" onClick={logoutGoogle}><LogOut/> Đăng xuất</button></div>}
      {user && <div className="mobile-tabs"><button className={activeView === 'devices' ? 'active' : ''} onClick={() => setActiveView('devices')}>Thiết bị của tôi</button><button className={activeView === 'play' ? 'active' : ''} onClick={() => setActiveView('play')}>Cùng chơi</button></div>}
      {user && activeView === 'devices' && <div className="my-device-panel"><h3>PC và MumuPlayer</h3>{myDevices.length === 0 ? <div className="empty-state">Chưa có emulator. Hãy đăng nhập Google trên Desktop Connector để tự đồng bộ.</div> : myDevices.map(device => <div className="device-card" key={`${device.pcName}-${device.deviceId}`}><div className="device-icon"><Gamepad2/></div><div><b>{device.deviceName}</b><small>{device.pcName}</small></div><span className="online-badge">Online</span></div>)}</div>}
      {user && activeView === 'play' && <div className="play-panel">
        <div className="create-room-row"><input value={roomName} onChange={e => setRoomName(e.target.value)} placeholder="Tên phòng"/><button className="primary" onClick={createRoom}>Tạo phòng</button></div>
        <label className="join"><Link2/><input value={room} onChange={e => setRoom(e.target.value.toUpperCase())} placeholder="Mã phòng"/><button onClick={() => void joinRoom()}>Tham gia</button></label>
        <div className="room-list"><h3>Danh sách phòng</h3>{myRooms.length === 0 ? <div className="empty-state">Chưa có phòng nào.</div> : myRooms.map(item => <div className="room-card" key={item.roomId}><div><b>{item.name}</b><small>Mã: {item.roomId}</small></div><div className="room-actions"><button onClick={() => void navigator.clipboard.writeText(item.roomId)}><Copy/> Copy mã</button><button onClick={() => void joinRoom(item.roomId)}>Vào phòng</button></div></div>)}</div>
        <div className="device-list"><b>Chọn emulator để add vào phòng</b>{myDevices.length === 0 ? <p>Chưa có emulator.</p> : myDevices.map(device => <button className="device-card add-device" key={device.deviceId} onClick={async () => {
          if (!room) { setApiError('Hãy tạo phòng hoặc chọn phòng trước.'); return }
          setApiError('')
          try {
            const response = await fetch(`${API_URL}/api/rooms/${room}/devices`, {
              method: 'POST',
              headers: { 'Content-Type': 'application/json' },
              body: JSON.stringify({ ownerSubject: user.subject, deviceId: device.deviceId, deviceName: device.deviceName, pcName: device.pcName })
            })
            if (!response.ok) throw new Error(`Không thể thêm emulator (HTTP ${response.status})`)
            const updatedRoom = await response.json()
            setDevices(updatedRoom.devices ?? [])
          } catch (error) {
            setApiError(error instanceof Error ? error.message : 'Không thể thêm emulator vào phòng.')
          }
        }}><div className="device-icon"><Gamepad2/></div><div><b>{device.deviceName}</b><small>{device.pcName} · {device.deviceId}</small></div><span>Thêm</span></button>)}</div>
      </div>}
    </section> : <section className="dashboard">
      <header><div className="brand"><Gamepad2/><b>Remote Mumu</b></div><div className="status"><span className={connected ? 'online' : ''}/>{connected ? 'Đã kết nối' : 'Ngoại tuyến'}</div><button className="icon" onClick={logoutGoogle}><LogOut/></button></header>
      {apiError && <p className="api-error" role="alert">{apiError}</p>}
      <div className="tabs">
        <button className={activeView === 'devices' ? 'active' : ''} onClick={() => setActiveView('devices')}>Thiết bị của tôi</button>
        <button className={activeView === 'play' ? 'active' : ''} onClick={() => setActiveView('play')}>Cùng chơi</button>
      </div>
      <div className="workspace">
        {activeView === 'devices' ? <aside><h3><Users/> Thiết bị của tôi</h3>
          {myDevices.length === 0 ? <p>Chưa có thiết bị nào.</p> : myDevices.map(device => <div className="device" key={device.deviceId}><span className={device.online ? 'online-dot' : ''}/>{device.deviceName}</div>)}
        </aside> : <aside><h3><Users/> Thành viên</h3><div className="member"><div>Y</div><span>{name}<small>Chủ phòng</small></span></div></aside>}
        <section className="screen">
          <div className="screen-toolbar">
            <span>Emulator #1</span>
            <span className="live">LIVE</span>
          </div>
          <div className="canvas">
            <div className="phone">
              {frame ? <img src={frame} alt="Emulator Frame" onPointerDown={onPointerDown} onPointerUp={onPointerUp} style={{ width: '100%', height: '100%', objectFit: 'contain', cursor: 'crosshair', touchAction: 'none' }} /> : <span>Chờ desktop agent</span>}
            </div>
          </div>
        </section>
        {activeView === 'play' && <aside className="chat"><h3>Trò chuyện</h3><div className="messages">{chat.map((item, i) => <div className="message" key={i}><b>{item.name}</b><span>{item.text}</span></div>)}</div><div className="composer"><input value={message} onChange={e => setMessage(e.target.value)} onKeyDown={e => e.key === 'Enter' && sendChat()} placeholder="Nhập tin nhắn…"/><button onClick={sendChat}><Send/></button></div></aside>}
      </div>
    </section>}
  </main>
}

createRoot(document.getElementById('root')!).render(<React.StrictMode><App/></React.StrictMode>)

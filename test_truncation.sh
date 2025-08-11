#!/bin/bash

# Test script to send data to honeypot and then analyze responses

echo "Starting LLMHoney honeypot in background..."
cd /home/andrew/dev/LLMHoney
dotnet run --project src/LLMHoney.Host &
HONEYPOT_PID=$!

# Wait for honeypot to start
echo "Waiting for honeypot to start..."
sleep 5

# Send some test data to trigger responses
echo "Sending test data to honeypot ports..."

# Check which ports are configured
echo "Available honeypot configurations:"
ls -la src/LLMHoney.Host/configs/

# Send test data to SSH port (typically 22)
echo "GET / HTTP/1.1\r\nHost: test\r\n\r\n" | nc -w 5 localhost 22 2>/dev/null && echo "SSH test sent"

# Send test data to HTTP port (typically 80)
echo "GET /test HTTP/1.1\r\nHost: test\r\n\r\n" | nc -w 5 localhost 80 2>/dev/null && echo "HTTP test sent" 

# Send test data to FTP port (typically 21)
echo "USER test\r\nPASS test\r\n" | nc -w 5 localhost 21 2>/dev/null && echo "FTP test sent"

# Wait for responses to be processed
echo "Waiting for responses to be processed..."
sleep 3

# Stop the honeypot
echo "Stopping honeypot..."
kill $HONEYPOT_PID 2>/dev/null
sleep 2

# Run diagnostic analysis
echo "Running diagnostic analysis..."
dotnet run --project src/LLMHoney.Host analyze

echo "Test complete!"

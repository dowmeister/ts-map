import express from 'express';
import cors from 'cors';
import { initializeRouting, router } from './routing/api';

const app = express();
const PORT = parseInt(process.env['PORT'] ?? '3001', 10);

app.use(cors());
app.use(express.json());
app.use('/api', router);

app.listen(PORT, () => {
  console.log(`[Server] Listening on port ${PORT}`);
  // Initialize routing after server is accepting connections
  initializeRouting();
});
